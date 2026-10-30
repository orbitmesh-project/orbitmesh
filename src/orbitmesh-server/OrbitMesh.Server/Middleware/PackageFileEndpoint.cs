using OrbitMesh.Server.Configuration;
using Microsoft.Extensions.Options;

namespace OrbitMesh.Server.Middleware;

/// <summary>
/// Serves package zip files under <c>/packages/{file}</c>. It is an endpoint mapped with the <c>door:package-download</c>
/// policy (see Program.cs), so who may download is decided like for any other endpoint; this only resolves and
/// streams the file.
/// </summary>
public static class PackageFileEndpoint
{
    public static IResult Handle(HttpContext context, IOptionsMonitor<OrbitMeshOptions> options, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("OrbitMesh.Server.PackageFiles");
        context.Request.Path.StartsWithSegments("/packages", out var remaining);
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
            return Results.Text("Package not found", statusCode: StatusCodes.Status404NotFound);
        }

        if (!File.Exists(fullPath))
        {
            logger.LogWarning("Package not found for {Path} (local path: {LocalPath})", context.Request.Path.ToString().ForLog(), fullPath);
            return Results.Text("Package not found", statusCode: StatusCodes.Status404NotFound);
        }

        logger.LogInformation("{Edge} downloading {Path}", context.Request.Headers[OrbitMeshHeaderNames.EdgeName].ToString().ForLog(), context.Request.Path.ToString().ForLog());
        return Results.File(fullPath, "application/zip");
    }
}
