namespace OrbitMesh.Server.Models;

public sealed class PackageInfo
{
    public required PackageDescription Package { get; set; }

    public PackageState State { get; set; } = PackageState.Unknown;

    public string? ConnectionId { get; set; }

    public bool IsConnected { get; set; }

    public DateTime LastUpdate { get; set; }

    public string? PackageVersion { get; set; }

    public string? OrbitMeshClientVersion { get; set; }

    public string? OrbitMeshClientType { get; set; }
}
