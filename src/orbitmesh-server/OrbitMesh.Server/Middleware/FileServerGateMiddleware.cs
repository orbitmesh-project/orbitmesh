using Microsoft.Extensions.Options;
using OrbitMesh.Server.Configuration;
using OrbitMesh.Server.Services;

namespace OrbitMesh.Server.Middleware;

/// <summary>
/// The one thing a static site needs from the Server before its files are served: a <c>FileServers</c> entry marked
/// <c>LocalhostOnly</c> answers nobody but the machine it runs on, whatever they present. Every other request goes on.
/// (The files themselves are public - no AccessKey - which is why a site that keeps a key in its own config file
/// must be restricted this way, or by the reverse proxy.)
/// </summary>
public sealed class FileServerGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IOptionsMonitor<OrbitMeshOptions> options, OrbitMeshMetrics metrics)
    {
        var path = context.Request.Path;
        var site = options.CurrentValue.FileServers
            .FirstOrDefault(fs => fs.Enable && path.StartsWithSegments(fs.Path, StringComparison.OrdinalIgnoreCase));
        if (site is { LocalhostOnly: true } && !IsLocalhost(context))
        {
            metrics.AccessDenied();
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Forbidden");
            return;
        }
        await next(context);
    }

    private static bool IsLocalhost(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        return ip != null && System.Net.IPAddress.IsLoopback(ip);
    }
}
