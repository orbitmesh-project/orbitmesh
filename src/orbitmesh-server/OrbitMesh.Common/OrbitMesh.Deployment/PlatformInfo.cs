using System.Xml.Serialization;

namespace OrbitMesh.Deployment;

/// <summary>One entry in <see cref="CompatibilityInfo.Platforms"/> - marks a specific OS as
/// supported/unsupported.</summary>
public sealed class PlatformInfo
{
    /// <summary>OSPlatform-style identifier, e.g. "Windows", "Linux", "OSX".</summary>
    [XmlAttribute("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>False to mark this OS as explicitly unsupported.</summary>
    [XmlAttribute("isCompliant")]
    public bool IsCompliant { get; set; } = true;
}
