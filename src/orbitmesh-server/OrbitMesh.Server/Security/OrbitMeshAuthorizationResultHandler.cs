using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using OrbitMesh.Server.Services;

namespace OrbitMesh.Server.Security;

/// <summary>
/// What happens once the policies have been evaluated: a request that passed goes on, a refused one is answered, and
/// the bookkeeping of the old gate is kept - metrics, the credential's last use, the audit line for changes made
/// through the Management API, and the failed-attempt count behind the IP lockout.
/// <para>The count only grows when the <i>door</i> refuses a caller that presented an AccessKey. A caller with no
/// credential, a stale cookie, or a request refused for lacking a scope (the door let it in) is not guessing anything.</para>
/// </summary>
public sealed class OrbitMeshAuthorizationResultHandler(
    OrbitMeshMetrics metrics,
    CredentialUsageTracker usageTracker,
    LoginAttemptLimiter limiter,
    ILoggerFactory loggerFactory) : IAuthorizationMiddlewareResultHandler
{
    private static readonly HashSet<string> MutatingMethods = new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "DELETE", "PATCH" };

    private readonly ILogger logger = loggerFactory.CreateLogger<OrbitMeshAuthorizationResultHandler>();

    // Separate "Audit" logger category, routed by NLog.config to its own long-retention file - keeps
    // "who changed what" readable instead of buried in the general request/connection log.
    private readonly ILogger audit = loggerFactory.CreateLogger("Audit");

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult result)
    {
        var state = context.GetRequestState();
        var remoteIp = context.Connection.RemoteIpAddress;

        if (state.LockedOut)
        {
            metrics.AccessDenied();
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsync("Too many failed attempts - try again later.");
            return;
        }

        // A hub connection is many requests (negotiate, then a socket or a poll loop): count the negotiation only.
        var countsAsAccess = !context.Request.Path.StartsWithSegments("/signalr", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.Value?.EndsWith("/negotiate", StringComparison.OrdinalIgnoreCase) == true;
        if (countsAsAccess)
        {
            metrics.AccessChecked();
        }
        if (state.CsrfRefused)
        {
            metrics.AccessDenied();
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Forbidden");
            return;
        }

        var door = policy.Requirements.OfType<IDoorRequirement>().FirstOrDefault();
        // The door let the caller in if everything passed, or if it did and the only refusal is a scope (checked past the door).
        var doorPassed = result.Succeeded || (door != null && state.DoorPassed);

        if (doorPassed)
        {
            if (countsAsAccess)
            {
                metrics.AccessGranted();
            }
            usageTracker.RecordUse(state.CredentialName);
            if (door?.AccessType == OrbitMeshAccessType.Management && MutatingMethods.Contains(context.Request.Method))
            {
                audit.LogInformation("{RemoteIp} credential='{Credential}' {Method} {Path}",
                    remoteIp, (state.CredentialName ?? "?").ForLog(), context.Request.Method.ForLog(), context.Request.Path.ToString().ForLog());
            }

            if (result.Succeeded)
            {
                await next(context);
                return;
            }

            var reason = result.AuthorizationFailure?.FailureReasons.Select(r => r.Message).FirstOrDefault(m => !string.IsNullOrEmpty(m)) ?? "Forbidden";
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync(reason);
            return;
        }

        metrics.AccessDenied();
        // Only a request that actually presented an AccessKey can be a guess worth counting. One with none (or with a
        // stale session cookie, which can't be guessed) is just unauthenticated - counting those would lock a user out
        // of their own IP the moment their session expires and the Console retries.
        if (state.PresentedAccessKey && limiter.RecordFailure(remoteIp))
        {
            logger.LogWarning("Locking out {RemoteIp} for repeated failed AccessKey attempts.", remoteIp);
        }
        logger.LogWarning(
            "Access denied for {RemoteIp} on {Path} (EdgeName={EdgeName}, PackageName={PackageName}, AccessType={AccessType}). " +
            "Check that a matching Edge/Credential is configured in appsettings.json.",
            remoteIp, context.Request.Path.ToString().ForLog(),
            context.Request.GetHeaderOrQuery(OrbitMeshHeaderNames.EdgeName).ForLog(),
            context.Request.GetHeaderOrQuery(OrbitMeshHeaderNames.PackageName).ForLog(),
            door?.AccessType ?? OrbitMeshAccessType.OrbitMesh);
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("Forbidden");
    }
}
