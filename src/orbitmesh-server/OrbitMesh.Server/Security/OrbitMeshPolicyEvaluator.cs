using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using OrbitMesh.Server.Services;

namespace OrbitMesh.Server.Security;

/// <summary>The claims of an authenticated caller, and the name of the "scheme" it authenticated through.</summary>
public static class OrbitMeshClaims
{
    public const string AuthenticationType = "OrbitMesh";
    public const string Scope = "scope";
    public const string Kind = "orbitmesh:kind";
    public const string Federated = "orbitmesh:federated";
}

/// <summary>What the authorization result handler needs to know about how a request fared while it was authenticated.</summary>
public sealed class RequestState
{
    /// <summary>The caller supplied an AccessKey (as opposed to nothing, or only a session cookie): only that can be a guess.</summary>
    public bool PresentedAccessKey { get; set; }

    public bool LockedOut { get; set; }

    /// <summary>The door requirement of the policy was met. Recorded by the handler itself: once any handler calls Fail()
    /// the framework no longer says which requirements failed, so this can't be read back from the result.</summary>
    public bool DoorPassed { get; set; }

    /// <summary>A request riding on a cookie that lacks the identity headers a script on another origin couldn't set.</summary>
    public bool CsrfRefused { get; set; }

    public string? CredentialName { get; set; }
}

public static class RequestStateExtensions
{
    private const string ItemKey = "OrbitMesh.RequestState";

    public static RequestState GetRequestState(this HttpContext context)
    {
        if (context.Items.TryGetValue(ItemKey, out var existing) && existing is RequestState state)
        {
            return state;
        }
        var created = new RequestState();
        context.Items[ItemKey] = created;
        return created;
    }
}

/// <summary>
/// Works out who is calling, for the endpoints that ask for authorization - and only for those. A request with an
/// AccessKey (header or query string) is that credential; one with the Console's session cookie is that person; one
/// with neither is anonymous. The result is an ordinary <see cref="ClaimsPrincipal"/>: its name, its kind, and one
/// <c>scope</c> claim per scope it holds right now.
/// <para>This is the one place identity is resolved for REST. It takes the place of an authentication scheme on purpose:
/// a scheme registered here would become the default and run on every request, static files and public endpoints
/// included, which is more work and more exposure than this server wants.</para>
/// </summary>
public sealed class OrbitMeshPolicyEvaluator(
    IAuthorizationService authorization,
    IOrbitMeshDirectory directory,
    SessionStore sessions,
    LoginAttemptLimiter limiter) : IPolicyEvaluator
{
    private static readonly HashSet<string> MutatingMethods = new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "DELETE", "PATCH" };

    public Task<AuthenticateResult> AuthenticateAsync(AuthorizationPolicy policy, HttpContext context)
    {
        var state = context.GetRequestState();

        // Checked before anything else, whatever the caller presents: a locked-out address gets nothing.
        if (limiter.IsLockedOut(context.Connection.RemoteIpAddress))
        {
            state.LockedOut = true;
            return Task.FromResult(AuthenticateResult.Fail("Too many failed attempts."));
        }

        // The AccessKey if one was presented, otherwise the Console's session cookie (as a pseudo key, see CallerKeys).
        var key = CallerKeys.FromRequest(context.Request, sessions, out var presented);
        context.SetEffectiveAccessKey(key);
        state.PresentedAccessKey = presented;

        // A cookie is sent by the browser on its own, so a request riding on it must also prove it was built by the
        // Console's script, not by a form on another page: the identity headers have to be real headers (a cross-origin
        // script can't set them without a CORS preflight, which is refused).
        if (CallerKeys.IsSessionKey(key) && MutatingMethods.Contains(context.Request.Method)
            && !context.Request.Headers.ContainsKey(OrbitMeshHeaderNames.EdgeName))
        {
            state.CsrfRefused = true;
            return Task.FromResult(AuthenticateResult.Fail("A state-changing request on a cookie needs the identity headers."));
        }

        var credential = directory.FindCredential(key);
        if (credential == null)
        {
            // Nothing presented (or only a stale cookie) is just anonymous; a key that matches nothing is a failure.
            return Task.FromResult(presented ? AuthenticateResult.Fail("The AccessKey matches no credential.") : AuthenticateResult.NoResult());
        }

        state.CredentialName = credential.Name;
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, credential.Name),
            new(OrbitMeshClaims.Kind, credential.Kind.ToString())
        };
        if (CallerKeys.IsSessionKey(key) && directory.GetFederatedGroups(key) != null)
        {
            claims.Add(new Claim(OrbitMeshClaims.Federated, bool.TrueString));
        }
        claims.AddRange(credential.Scopes.Select(scope => new Claim(OrbitMeshClaims.Scope, scope)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, OrbitMeshClaims.AuthenticationType, ClaimTypes.Name, ClaimTypes.Role));
        context.User = principal;
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, OrbitMeshClaims.AuthenticationType)));
    }

    public async Task<PolicyAuthorizationResult> AuthorizeAsync(AuthorizationPolicy policy, AuthenticateResult authenticationResult, HttpContext context, object? resource)
    {
        var result = await authorization.AuthorizeAsync(context.User, resource, policy);
        if (result.Succeeded)
        {
            return PolicyAuthorizationResult.Success();
        }
        return authenticationResult.Succeeded ? PolicyAuthorizationResult.Forbid(result.Failure) : PolicyAuthorizationResult.Challenge();
    }
}
