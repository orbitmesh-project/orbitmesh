using OrbitMesh.Updating;

namespace OrbitMesh.Edge.Configuration;

public sealed class EdgeOptions
{
    public const string SectionName = "Edge";

    public required string OrbitMeshServerUri { get; set; }

    public required string OrbitMeshAccessKey { get; set; }

    /// <summary>Refuse to start when <see cref="OrbitMeshServerUri"/> is plain HTTP to a non-loopback
    /// host (the AccessKey would cross the network unencrypted). Off by default - the Edge only logs a
    /// warning - so enabling TLS on the Server is a deliberate step, not a side effect of an update.</summary>
    public bool RequireHttps { get; set; }

    public string LocalPackagesDirectory { get; set; } = "Packages";

    public string? EdgeName { get; set; }

    public int ReportPackageUsageIntervalMs { get; set; } = 1000;

    public int ShutdownPackageTimeoutMs { get; set; } = 10_000;

    public UpdateOptions UpdateOptions { get; set; } = new() { ProjectSlug = "orbitmesh-edge" };
}
