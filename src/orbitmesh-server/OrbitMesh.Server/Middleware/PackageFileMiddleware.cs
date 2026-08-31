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

        var relativePath = remaining.Value?.TrimStart('/') ?? string.Empty;
        var root = Path.GetFullPath(options.CurrentValue.PackagesRootDirectory);
        // Path.Combine ignores `root` entirely when relativePath is itself rooted (e.g. "C:/..." or
        // a leading "/"/"\") - stripping ".." alone doesn't stop that. GetFullPath + a contained-in-root
        // check afterward is required, same pattern as PackageInstance.ResolveContained on the Edge side.
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;

        if (fullPath != root && !fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            logger.LogWarning("Rejected package request outside the packages root: {Path} (resolved to {LocalPath})", context.Request.Path.ToString().ForLog(), fullPath);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("Package not found");
            return;
        }

        if (!File.Exists(fullPath))
        {
            logger.LogWarning("Package not found for {Path} (local path: {LocalPath})", context.Request.Path.ToString().ForLog(), fullPath);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("Package not found");
            return;
        }

        logger.LogInformation("{Edge} downloading {Path}", context.Request.Headers[OrbitMeshHeaderNames.EdgeName].ToString().ForLog(), context.Request.Path.ToString().ForLog());
        context.Response.ContentType = "application/zip";
        await context.Response.SendFileAsync(fullPath);
    }
}
