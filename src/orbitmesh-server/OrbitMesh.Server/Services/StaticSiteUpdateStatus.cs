using OrbitMesh.Updating;

namespace OrbitMesh.Server.Services;

/// <summary>Latest known update-check result for one static site (Console, or any other) - the
/// static-site equivalent of <see cref="UpdateStatus"/>, one instance per <c>FileServerOptions</c>
/// entry that has an <c>UpdateProjectSlug</c> configured.</summary>
public sealed class StaticSiteUpdateStatus(string path, string physicalPath, string projectSlug)
{
    public string Path { get; } = path;

    public string PhysicalPath { get; } = physicalPath;

    public string ProjectSlug { get; } = projectSlug;

    public string? CurrentVersion { get; set; }

    public string? LatestVersion { get; set; }

    public string? Changelog { get; set; }

    public string? ZipUrl { get; set; }

    public DateTimeOffset? LastCheckedUtc { get; set; }

    public string? Error { get; set; }

    public bool IsUpdateAvailable => UpdateVersionComparer.IsNewer(LatestVersion, CurrentVersion);
}
