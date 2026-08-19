using OrbitMesh.Server.Configuration;
using Microsoft.Extensions.Options;

namespace OrbitMesh.Server.Middleware;

/// <summary>
/// Serves package zip files under <c>/packages/{file}</c>. Access is already fully gated upstream by
/// <see cref="AccessKeyAuthenticationMiddleware"/> (verified live: an unauthenticated request to this
/// path gets 403 before it ever reaches here) - this middleware only needs to resolve and stream the file.
/// </summary>
public sealed class PackageFileMiddleware(RequestDelegate next, ILogger<PackageFileMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IOptionsMonitor<OrbitMeshOptions> options)
    {
        if (!context.Request.Path.StartsWithSegments("/packages", out var remaining))
        {
            await next(context);
            return;
        }

        var relativePath = remaining.Value?.TrimStart('/').Replace("..", string.Empty) ?? string.Empty;
        var fullPath = Path.Combine(Path.GetFullPath(options.CurrentValue.PackagesRootDirectory), relativePath);

        if (!File.Exists(fullPath))
        {
            logger.LogWarning("Package not found for {Path} (local path: {LocalPath})", context.Request.Path, fullPath);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("Package not found");
            return;
        }

        logger.LogInformation("{Edge} downloading {Path}", context.Request.Headers[OrbitMeshHeaderNames.EdgeName], context.Request.Path);
        context.Response.ContentType = "application/zip";
        await context.Response.SendFileAsync(fullPath);
    }
}
