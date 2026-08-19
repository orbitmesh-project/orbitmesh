using System.Xml.Serialization;

namespace OrbitMesh.Deployment;

/// <summary>The deserialized form of a package's <c>PackageInfo.xml</c> manifest - name, author,
/// description, its settings schema, dependencies and platform compatibility. Read via
/// <see cref="PackageManifestHelper"/>; available at runtime as <c>PackageHost.PackageManifest</c>.</summary>
[XmlRoot("Package")]
public sealed class PackageManifest
{
    /// <summary>This SDK's own assembly version.</summary>
    public static Version OrbitMeshVersion { get; } = typeof(PackageManifest).Assembly.GetName().Version ?? new Version(1, 0, 0);

    /// <summary>The package's name.</summary>
    [XmlAttribute("Name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The package's version - typically stamped from the project's own <c>.csproj</c>
    /// <c>&lt;Version&gt;</c> at build time rather than maintained here by hand.</summary>
    [XmlAttribute("Version")]
    public string Version { get; set; } = "1.0.0";

    /// <summary>The package's author, shown in the Console.</summary>
    [XmlAttribute("Author")]
    public string? Author { get; set; }

    /// <summary>A short description, shown in the Console.</summary>
    [XmlAttribute("Description")]
    public string? Description { get; set; }

    /// <summary>An optional homepage/documentation URL, shown in the Console.</summary>
    [XmlAttribute("URL")]
    public string? URL { get; set; }

    /// <summary>An icon filename bundled alongside the manifest, shown in the Console.</summary>
    [XmlAttribute("Icon")]
    public string? Icon { get; set; }

    /// <summary>Reserved for a future capability - currently unused.</summary>
    [XmlAttribute("EnableControlHub")]
    public bool EnableControlHub { get; set; }

    /// <summary>Executable invoked by the Edge. Defaults to "{Name}.dll" run through "dotnet" when unset.</summary>
    [XmlAttribute("ExecutableFilename")]
    public string? ExecutableFilename { get; set; }

    /// <summary>Which Edge runtime <see cref="ExecutableFilename"/> needs - see <see cref="PackageRuntime"/>.
    /// An Edge refuses to start a package declaring the other runtime instead of attempting to launch
    /// it (see OrbitMesh.Edge's PackageInstance/orbitmesh-edge's package_instance.py).</summary>
    [XmlAttribute("Runtime")]
    public PackageRuntime Runtime { get; set; } = PackageRuntime.DotNet;

    /// <summary>If true, the Edge asks the Server to resend this package's last-known telemetry item
    /// values as soon as it starts, instead of waiting for the package to request them itself.</summary>
    [XmlAttribute("RequestLastTelemetryItemsOnStart")]
    public bool RequestLastTelemetryItemsOnStart { get; set; }

    /// <summary>Reserved for a future capability - currently unused.</summary>
    [XmlAttribute("PackageUrl")]
    public string? PackageUrl { get; set; }

    /// <summary>The settings this package declares - name, type, default value, whether required.</summary>
    [XmlArray("Settings")]
    [XmlArrayItem("Setting")]
    public List<PackageSetting> Settings { get; set; } = new();

    /// <summary>The .NET target platform and OS(es) this package supports.</summary>
    [XmlElement("Compatibility")]
    public CompatibilityInfo Compatibility { get; set; } = new();

    /// <summary>Other packages/libraries this one depends on - documentation only, not enforced by
    /// the SDK itself.</summary>
    [XmlArray("Dependencies")]
    [XmlArrayItem("Dependency")]
    public List<DependencyInfo> Dependencies { get; set; } = new();

    /// <summary>True if <see cref="Compatibility"/> allows running on the current OS.</summary>
    public bool IsCompliantOnCurrentPlatform() => Compatibility.IsCompliantOnCurrentPlatform();
}
