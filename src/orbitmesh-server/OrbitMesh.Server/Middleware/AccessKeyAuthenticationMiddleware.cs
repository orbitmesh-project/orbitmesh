using OrbitMesh.Server.Configuration;
using OrbitMesh.Server.Services;
using Microsoft.Extensions.Options;

namespace OrbitMesh.Server.Middleware;

/// <summary>
/// Gate for REST calls (EdgeName/PackageName/AccessKey via header or querystring). SignalR hub
/// connections are authenticated separately in each Hub's OnConnectedAsync.
/// </summary>
public sealed class AccessKeyAuthenticationMiddleware(RequestDelegate next, ILogger<AccessKeyAuthenticationMiddleware> logger, ILoggerFactory loggerFactory)
{
    // Separate "Audit" logger category, routed by NLog.config to its own long-retention file - keeps
    // "who changed what" readable instead of buried in the general request/connection log.
    private readonly ILogger auditLogger = loggerFactory.CreateLogger("Audit");

    private static readonly HashSet<string> MutatingMethods = new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "DELETE", "PATCH" };

    public async Task InvokeAsync(
        HttpContext context,
        IOptionsMonitor<OrbitMeshOptions> options,
        IOrbitMeshDirectory directory,
        OrbitMeshMetrics metrics,
        LoginAttemptLimiter attemptLimiter,
        CredentialUsageTracker usageTracker)
    {
        // Double-slash paths (the console builds some URLs as `{origin}/` + `/rest/...`) are already
        // collapsed by the app.Use(...) middleware registered before UseRouting() in Program.cs, which
        // runs ahead of this one - context.Request.Path is guaranteed normalized by the time it gets here.
        var path = context.Request.Path;

        var matchingFileServer = options.CurrentValue.FileServers
            .FirstOrDefault(fs => fs.Enable && path.StartsWithSegments(fs.Path, StringComparison.OrdinalIgnoreCase));
        if (matchingFileServer != null && (!matchingFileServer.LocalhostOnly || IsLocalhost(context)))
        {
            await next(context);
            return;
        }

        // SignalR hub connections authenticate themselves in each Hub's OnConnectedAsync (with the
        // correct per-hub access type), so the negotiate/connect HTTP requests are not gated here.
        if (path.StartsWithSegments("/signalr", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        // First-run bootstrap has no credential to authenticate with yet - GetSetupStatus is read-only
        // (just "does any credential exist"), and CreateAdmin enforces its own "only while zero
        // credentials exist" check inside the config writer lock, so neither needs a gate here.
        if (path.StartsWithSegments("/rest/management/setup", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        // The Prometheus scrape endpoint and OpenAPI docs are not part of the OrbitMesh protocol
        // and have no AccessKey to present; they're not sensitive enough to warrant gating.
        if (path.StartsWithSegments("/metrics", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/openapi", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var remoteIp = context.Connection.RemoteIpAddress;
        if (attemptLimiter.IsLockedOut(remoteIp))
        {
            metrics.AccessDenied();
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsync("Too many failed attempts - try again later.");
            return;
        }

        var edgeName = context.Request.GetHeaderOrQuery(OrbitMeshHeaderNames.EdgeName);
        var packageName = context.Request.GetHeaderOrQuery(OrbitMeshHeaderNames.PackageName);
        var accessKey = context.Request.GetHeaderOrQuery(OrbitMeshHeaderNames.AccessKey);

        var accessType = OrbitMeshAccessType.OrbitMesh;
        if (path.StartsWithSegments("/rest/controller", StringComparison.OrdinalIgnoreCase))
        {
            accessType = OrbitMeshAccessType.Controller;
        }
        else if (path.StartsWithSegments("/rest/management", StringComparison.OrdinalIgnoreCase))
        {
            accessType = OrbitMeshAccessType.Management;
        }

        metrics.AccessChecked();
        if (directory.CheckAccess(edgeName, packageName, accessKey, accessType))
        {
            metrics.AccessGranted();
            attemptLimiter.RecordSuccess(remoteIp);
            var credentialName = directory.GetCredentialName(accessKey);
            usageTracker.RecordUse(credentialName);
            if (accessType == OrbitMeshAccessType.Management && MutatingMethods.Contains(context.Request.Method))
            {
                auditLogger.LogInformation("{RemoteIp} credential='{Credential}' {Method} {Path}",
                    remoteIp, (credentialName ?? "?").ForLog(), context.Request.Method.ForLog(), path.ToString().ForLog());
            }
            await next(context);
            return;
        }

        metrics.AccessDenied();
        if (attemptLimiter.RecordFailure(remoteIp))
        {
            logger.LogWarning("Locking out {RemoteIp} for repeated failed AccessKey attempts.", remoteIp);
        }
        logger.LogWarning(
            "Access denied for {RemoteIp} on {Path} (EdgeName={EdgeName}, PackageName={PackageName}, AccessType={AccessType}). " +
            "Check that a matching Edge/Credential is configured in appsettings.json.",
            remoteIp, context.Request.Path.ToString().ForLog(), edgeName.ForLog(), packageName.ForLog(), accessType);
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("Forbidden");
    }

    private static bool IsLocalhost(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        return ip != null && (System.Net.IPAddress.IsLoopback(ip));
    }
}
