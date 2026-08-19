namespace OrbitMesh;

/// <summary>What the Server tells an Edge about one package it should run - not used by package
/// authors directly, this is Edge/Server plumbing.</summary>
public sealed class PackageDescription
{
    /// <summary>The package instance's name.</summary>
    public required string Name { get; set; }

    /// <summary>The repository zip filename to download/run, e.g. "MyPackage.zip".</summary>
    public string? PackageFile { get; set; }

    /// <summary>Whether the Edge should launch this package automatically.</summary>
    public bool AutoStart { get; set; }

    /// <summary>The access key this package instance should connect with, only populated when
    /// explicitly requested (the Server otherwise omits it from this description).</summary>
    public string? AccessKey { get; set; }

    /// <summary>Restart-after-crash policy for this instance, or null to use the Server's global default.</summary>
    public RecoveryOptions? RecoveryOptions { get; set; }
}
