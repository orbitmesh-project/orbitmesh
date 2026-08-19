using System.Xml.Serialization;

namespace OrbitMesh.Deployment;

/// <summary>One <c>&lt;Dependency&gt;</c> declared in a package's manifest - documentation only, not
/// enforced by the SDK itself (the actual assembly reference is what matters at build time).</summary>
public sealed class DependencyInfo
{
    /// <summary>The dependency's name.</summary>
    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The dependency's version.</summary>
    [XmlAttribute("version")]
    public string? Version { get; set; }
}
