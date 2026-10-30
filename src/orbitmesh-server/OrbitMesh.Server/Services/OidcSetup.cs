using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using OrbitMesh.Server.Configuration;

namespace OrbitMesh.Server.Services;

/// <summary>
/// Single sign-on through an OpenID Connect provider (built and tested against Authelia, but nothing here is
/// Authelia-specific). The standard ASP.NET Core OIDC handler runs the authorization-code flow with PKCE;
/// its only job is to prove who the user is. Once it has, <c>OnTicketReceived</c> maps the user's groups to
/// OrbitMesh scopes (<see cref="OidcRoleMapper"/>), starts the very same session as a password sign-in
/// (<see cref="SessionStore"/>) and redirects to the Console - the handler's own cookie is never issued.
/// The handler is wired once at startup, so changing the provider settings needs a restart; the role
/// mappings are read live.
/// </summary>
public static class OidcSetup
{
    public const string Scheme = "orbitmesh-oidc";

    private const string TemporarySignInScheme = "orbitmesh-oidc-unused";

    public const string CallbackPath = "/rest/auth/oidc/callback";

    public const string ErrorUrlKey = "orbitmesh.errorUrl";

    // Appended to the Console's error URL so its sign-in page can say why the user is back there.
    public const string ErrorQueryParameter = "login_error";

    public static void AddOrbitMeshOidc(this WebApplicationBuilder builder, OidcOptions oidc)
    {
        // State and nonce cookies are protected with Data Protection; without a persisted key ring a restart
        // between "redirect to the provider" and "back from it" (e.g. a self-update) would fail that sign-in.
        builder.Services.AddDataProtection()
            .SetApplicationName("OrbitMesh.Server")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "keys", "dataprotection")));

        // The OIDC handler insists on a separate scheme to sign the verified user into. That scheme is never
        // actually used (OnTicketReceived below ends the exchange with our own session cookie), but it must
        // exist and differ from the OIDC scheme: with only one scheme registered ASP.NET makes it the default,
        // and the handler then authenticates against itself until the stack overflows. Having two also means
        // there is no default scheme, so UseAuthentication() does no per-request work beyond the callback path.
        builder.Services.AddAuthentication()
            .AddCookie(TemporarySignInScheme, options => options.Cookie.Name = "orbitmesh_oidc_unused")
            .AddOpenIdConnect(Scheme, options =>
        {
            options.SignInScheme = TemporarySignInScheme;
            options.Authority = oidc.Authority;
            options.ClientId = oidc.ClientId;
            options.ClientSecret = oidc.ClientSecret;
            options.RequireHttpsMetadata = oidc.RequireHttpsMetadata;
            options.ResponseType = "code";
            options.UsePkce = true;
            options.CallbackPath = CallbackPath;
            options.GetClaimsFromUserInfoEndpoint = true;
            options.SaveTokens = false;
            // Keep the provider's claim names ("preferred_username", "groups") instead of the legacy WS-* URIs.
            options.MapInboundClaims = false;
            options.Scope.Clear();
            foreach (var scope in oidc.RequestedScopes.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                options.Scope.Add(scope);
            }
            if (!options.Scope.Contains("openid"))
            {
                options.Scope.Add("openid");
            }
            options.ClaimActions.MapJsonKey(oidc.GroupsClaim, oidc.GroupsClaim);
            options.ClaimActions.MapJsonKey(oidc.UsernameClaim, oidc.UsernameClaim);

            // Lax rather than the handler's default None: the provider redirects back with a top-level GET,
            // which Lax cookies accompany, and None requires Secure - which a LAN deployment over plain HTTP
            // to the Server (TLS only at the provider) doesn't have.
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.NonceCookie.SameSite = SameSiteMode.Lax;
            options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

            options.Events.OnTicketReceived = HandleTicketAsync;
            options.Events.OnRemoteFailure = HandleRemoteFailureAsync;
        });
    }

    private static Task HandleTicketAsync(TicketReceivedContext context)
    {
        var services = context.HttpContext.RequestServices;
        var oidc = services.GetRequiredService<IOptionsMonitor<OrbitMeshOptions>>().CurrentValue.Auth.Oidc;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Audit");
        var sessions = services.GetRequiredService<SessionStore>();
        var remoteIp = context.HttpContext.Connection.RemoteIpAddress;

        var principal = context.Principal;
        var login = principal?.FindFirst(oidc.UsernameClaim)?.Value ?? principal?.FindFirst("sub")?.Value;
        var groups = principal?.FindAll(oidc.GroupsClaim).Select(c => c.Value).ToList() ?? [];

        if (string.IsNullOrWhiteSpace(login))
        {
            logger.LogWarning("{RemoteIp} single sign-on refused: the provider sent neither '{Claim}' nor 'sub'.", remoteIp, oidc.UsernameClaim.ForLog());
            return Redirect(context, "oidc_failed");
        }

        // Everything this account may do is decided by its groups: no mapping, no sign-in.
        if (OidcRoleMapper.ScopesFor(oidc, groups).Count == 0)
        {
            logger.LogWarning("{RemoteIp} single sign-on refused for '{Login}': none of its groups ({Groups}) is mapped to any scope.",
                remoteIp, login.ForLog(), string.Join(", ", groups).ForLog());
            return Redirect(context, "oidc_denied");
        }

        var name = OidcRoleMapper.SessionNamePrefix + login;
        var (token, session) = sessions.Create(name, isFederated: true, groups, remoteIp?.ToString());
        context.Response.SetSessionCookie(token, session.AbsoluteExpiryUtc - DateTime.UtcNow);
        services.GetRequiredService<CredentialUsageTracker>().RecordUse(name);
        logger.LogInformation("{RemoteIp} signed in '{Name}' (single sign-on, groups: {Groups})", remoteIp, name.ForLog(), string.Join(", ", groups).ForLog());

        // The handler moves the target the sign-in was started with from Properties.RedirectUri to ReturnUri.
        context.Response.Redirect(context.ReturnUri is { Length: > 0 } target && IsLocal(target) ? target : "/");
        context.HandleResponse();
        return Task.CompletedTask;
    }

    private static Task HandleRemoteFailureAsync(RemoteFailureContext context)
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Audit");
        logger.LogWarning("{RemoteIp} single sign-on failed: {Reason}", context.HttpContext.Connection.RemoteIpAddress, context.Failure?.Message.ForLog());
        Redirect(context, "oidc_failed");
        context.HandleResponse();
        return Task.CompletedTask;
    }

    private static Task Redirect(TicketReceivedContext context, string error)
    {
        RedirectWithError(context.Response, context.Properties, error);
        context.HandleResponse();
        return Task.CompletedTask;
    }

    private static void Redirect(RemoteFailureContext context, string error) =>
        RedirectWithError(context.Response, context.Properties, error);

    private static void RedirectWithError(HttpResponse response, AuthenticationProperties? properties, string error)
    {
        var target = properties?.Items.TryGetValue(ErrorUrlKey, out var errorUrl) == true && errorUrl is { Length: > 0 } && IsLocal(errorUrl) ? errorUrl : "/";
        var separator = target.Contains('?') ? '&' : '?';
        response.Redirect($"{target}{separator}{ErrorQueryParameter}={error}");
    }

    // The target round-trips through the provider inside protected state, but it is only ever used as a
    // redirect, so insist on a same-site relative path regardless.
    private static bool IsLocal(string url) =>
        url.Length > 0 && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
}
