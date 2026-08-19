using System.Runtime.InteropServices;
using System.Xml.Serialization;

namespace OrbitMesh.Deployment;

/// <summary>The <c>&lt;Compatibility&gt;</c> element of a package's manifest - target framework and,
/// optionally, per-OS compliance.</summary>
public sealed class CompatibilityInfo
{
    /// <summary>Reserved for a future minimum-OrbitMesh-version check - currently unused.</summary>
    [XmlAttribute("orbitmeshVersion")]
    public string? OrbitMeshVersion { get; set; }

    /// <summary>.NET TFM this package targets, e.g. "net10.0".</summary>
    [XmlAttribute("dotNetTargetPlatform")]
    public string? DotNetTargetPlatform { get; set; }

    /// <summary>Explicit per-OS compliance overrides - only needed to mark an OS as unsupported (see
    /// <see cref="PlatformInfo.IsCompliant"/>); an OS not listed here is assumed compliant.</summary>
    [XmlArray("Platforms")]
    [XmlArrayItem("Platform")]
    public List<PlatformInfo> Platforms { get; set; } = new();

    /// <summary>True unless the current OS is explicitly listed in <see cref="Platforms"/> with
    /// <see cref="PlatformInfo.IsCompliant"/> set to false.</summary>
    public bool IsCompliantOnCurrentPlatform()
    {
        var current = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "OSX"
            : "Linux";
        return !Platforms.Any(p => p.Id.Equals(current, StringComparison.OrdinalIgnoreCase) && !p.IsCompliant);
    }
}
