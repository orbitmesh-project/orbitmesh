using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OrbitMesh.Server.Configuration;
using OrbitMesh.Server.Services;

namespace OrbitMesh.Server.Controllers;

/// <summary>
/// How a person signs in to the Console. <c>POST rest/auth/login</c> trades a username and password for a
/// session cookie (HttpOnly, SameSite=Strict); <c>rest/auth/oidc/login</c> does the same through an OpenID
/// Connect provider such as Authelia (see <see cref="OidcSetup"/>). Neither hands out anything the page's
/// script can read, replacing the AccessKey cookie the Console used to keep. Machine callers (Edges,
/// packages, dashboards) don't come through here - they keep presenting their AccessKey.
/// Unauthenticated by necessity (AccessKeyAuthenticationMiddleware lets <c>/rest/auth</c> through), so
/// everything here is bounded: login attempts count against the same IP lockout as AccessKey guesses.
/// </summary>
[ApiController]
[Route("rest/auth")]
public sealed class AuthController(
    IOptionsMonitor<OrbitMeshOptions> options,
    IOrbitMeshDirectory directory,
    IOrbitMeshConfigWriter configWriter,
    SessionStore sessions,
    LoginAttemptLimiter attemptLimiter,
    CredentialUsageTracker usageTracker,
    OrbitMeshMetrics metrics,
    ILoggerFactory loggerFactory) : ControllerBase
{
    // Same "Audit" category as the Management API's changes (see AccessKeyAuthenticationMiddleware).
    private readonly ILogger audit = loggerFactory.CreateLogger("Audit");

    public sealed record LoginRequest(string? Name, string? Password);

    public sealed record SessionResponse(string Name, IReadOnlyList<string> Scopes, bool IsFederated, DateTime ExpiresUtc);

    /// <summary>What the sign-in page needs to know before showing anything.</summary>
    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        var auth = options.CurrentValue.Auth;
        return Ok(new
        {
            auth.LocalLoginEnabled,
            Oidc = new { Enabled = auth.Oidc.IsUsable && HttpContext.RequestServices.GetService<IAuthenticationSchemeProvider>() != null, auth.Oidc.DisplayName },
            auth.SessionIdleMinutes
        });
    }

    [HttpPost("login")]
    [RequestSizeLimit(4096)]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        if (attemptLimiter.IsLockedOut(remoteIp))
        {
            metrics.AccessDenied();
            return StatusCode(StatusCodes.Status429TooManyRequests, "Too many failed attempts - try again later.");
        }
        if (!options.CurrentValue.Auth.LocalLoginEnabled)
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Password sign-in is disabled on this server.");
        }
        if (!CallerKeys.IsSameOrigin(Request))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Forbidden");
        }
        if (string.IsNullOrEmpty(request.Name) || string.IsNullOrEmpty(request.Password) || request.Name.Length > 256 || request.Password.Length > 1024)
        {
            return BadRequest("A name and a password are required.");
        }

        // Only Human credentials sign in: a Machine key is for an Edge or a package and must never double as a login.
        var credential = options.CurrentValue.Credentials.FirstOrDefault(c =>
            c.Kind == CredentialKind.Human && c.Enable && c.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase));

        if (credential == null || !VerifyPassword(credential, request.Name, request.Password))
        {
            if (credential == null)
            {
                PasswordHasher.BurnVerify(request.Password);
            }
            metrics.AccessDenied();
            if (attemptLimiter.RecordFailure(remoteIp))
            {
                audit.LogWarning("Locking out {RemoteIp} for repeated failed sign-in attempts.", remoteIp);
            }
            audit.LogWarning("{RemoteIp} sign-in refused for '{Name}'", remoteIp, request.Name.ForLog());
            return StatusCode(StatusCodes.Status403Forbidden, "Invalid credentials.");
        }
        if (credential.Scopes.Count == 0)
        {
            return StatusCode(StatusCodes.Status403Forbidden, "This account holds no scopes, so it has nothing to do in the Console.");
        }

        UpgradeHashIfNeeded(credential.Name, request.Name, request.Password);
        return StartSession(credential.Name, isFederated: false, groups: null, "password");
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        if (CallerKeys.IsSameOrigin(Request) && Request.Cookies.TryGetValue(CallerKeys.SessionCookieName, out var token))
        {
            var session = sessions.TryGetByToken(token, touch: false);
            sessions.Revoke(token);
            if (session != null)
            {
                audit.LogInformation("{RemoteIp} signed out '{Name}'", HttpContext.Connection.RemoteIpAddress, session.Name.ForLog());
            }
        }
        Response.ClearSessionCookie();
        return Ok();
    }

    /// <summary>Who the session cookie belongs to, with the scopes it holds right now - also what the Console
    /// calls periodically to keep the session from idling out while it is open.</summary>
    [HttpGet("me")]
    public ActionResult<SessionResponse> Me()
    {
        var key = CallerKeys.FromSessionCookie(Request, sessions);
        var credential = directory.FindCredential(key);
        var session = sessions.TryGetById(CallerKeys.SessionIdOf(key));
        if (credential == null || session == null)
        {
            return Unauthorized();
        }
        return new SessionResponse(session.Name, credential.Scopes, session.IsFederated, sessions.ExpiresAtUtc(session));
    }

    /// <summary>Starts the OpenID Connect sign-in: sends the browser to the provider, which sends it back to
    /// <c>rest/auth/oidc/callback</c> (handled by the OIDC authentication handler, see <see cref="OidcSetup"/>).</summary>
    [HttpGet("oidc/login")]
    public IActionResult OidcLogin(string? returnUrl, string? errorUrl)
    {
        if (!options.CurrentValue.Auth.Oidc.IsUsable || HttpContext.RequestServices.GetService<IAuthenticationSchemeProvider>() == null)
        {
            return NotFound();
        }
        // Local paths only: this is where the browser lands afterwards, and an absolute URL would turn the
        // endpoint into an open redirector.
        var returnTo = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
        var errorTo = !string.IsNullOrEmpty(errorUrl) && Url.IsLocalUrl(errorUrl) ? errorUrl : returnTo;
        var properties = new AuthenticationProperties { RedirectUri = returnTo };
        properties.Items[OidcSetup.ErrorUrlKey] = errorTo;
        return Challenge(properties, OidcSetup.Scheme);
    }

    private IActionResult StartSession(string name, bool isFederated, IEnumerable<string>? groups, string method)
    {
        var (token, session) = sessions.Create(name, isFederated, groups, HttpContext.Connection.RemoteIpAddress?.ToString());
        Response.SetSessionCookie(token, session.AbsoluteExpiryUtc - DateTime.UtcNow);
        usageTracker.RecordUse(name);
        metrics.AccessGranted();
        audit.LogInformation("{RemoteIp} signed in '{Name}' ({Method})", HttpContext.Connection.RemoteIpAddress, name.ForLog(), method);
        var key = CallerKeys.SessionKeyPrefix + session.Id;
        return Ok(new SessionResponse(name, directory.FindCredential(key)?.Scopes ?? [], isFederated, sessions.ExpiresAtUtc(session)));
    }

    private bool VerifyPassword(CredentialOptions credential, string typedName, string password)
    {
        if (PasswordHasher.IsHashed(credential.AccessKey))
        {
            return PasswordHasher.Verify(credential.AccessKey, password);
        }
        // An account created before sessions existed: its stored hash is of the SHA-1 the old Console sent.
        return AccessKeyHasher.IsHashed(credential.AccessKey)
            && AccessKeyHasher.Verify(credential.AccessKey, PasswordHasher.LegacyAccessKey(typedName, password));
    }

    // A legacy hash is replaced by a real password hash on the account's first sign-in (which also ends the
    // old "the hash is the bearer token" behaviour for that account), and a hash made with fewer iterations
    // than the current count is brought up to date the same way.
    private void UpgradeHashIfNeeded(string credentialName, string typedName, string password)
    {
        var stored = options.CurrentValue.Credentials.First(c => c.Name.Equals(credentialName, StringComparison.OrdinalIgnoreCase)).AccessKey;
        if (PasswordHasher.IsHashed(stored) && !PasswordHasher.NeedsRehash(stored))
        {
            return;
        }
        try
        {
            configWriter.Update(cfg =>
            {
                var target = cfg.Credentials.FirstOrDefault(c => c.Name.Equals(credentialName, StringComparison.OrdinalIgnoreCase));
                // Only if it still holds the hash that was just verified - an admin may have reset it meanwhile.
                if (target != null && target.AccessKey == stored)
                {
                    target.AccessKey = PasswordHasher.Hash(password);
                }
            });
        }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetRequiredService<ILogger<AuthController>>()
                .LogWarning(ex, "Couldn't upgrade the stored password hash of '{Name}' - it will be retried at the next sign-in.", credentialName.ForLog());
        }
    }
}
