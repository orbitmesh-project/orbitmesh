namespace OrbitMesh;

/// <summary>An Edge process's identity and environment, as reported to the Server on connect and
/// shown in the Console's Edges page.</summary>
public sealed class EdgeDescription
{
    /// <summary>The Edge's configured name.</summary>
    public required string EdgeName { get; set; }

    /// <summary>A GUID the Edge generated on first run and persists locally - see
    /// <see cref="OrbitMeshHeaderNames.InstanceId"/>. Empty for an older Edge build.</summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>The host machine's name.</summary>
    public required string MachineName { get; set; }

    /// <summary>The OS version.</summary>
    public required string OSVersion { get; set; }

    /// <summary>The OS's human-readable name/caption.</summary>
    public required string OSCaption { get; set; }

    /// <summary>The OS platform identifier (Windows/Linux/OSX).</summary>
    public required string Platform { get; set; }

    /// <summary>The .NET runtime version.</summary>
    public required string CLRVersion { get; set; }

    /// <summary>The .NET runtime implementation (e.g. ".NET" vs ".NET Framework").</summary>
    public required string CLRImplementation { get; set; }

    /// <summary>The target framework moniker the Edge itself was built for.</summary>
    public required string FxVersion { get; set; }

    /// <summary>The Edge's own version.</summary>
    public required string Version { get; set; }

    /// <summary>Which <see cref="Deployment.PackageRuntime"/> this Edge can run - "DotNet" for
    /// OrbitMesh.Edge, "Python" for orbitmesh-edge. Not `required`: an older Edge build that predates
    /// this field simply omits it, and defaults to "DotNet" here rather than failing to deserialize.
    /// Lets the Console warn before deploying a package whose manifest declares the other runtime.</summary>
    public string Runtime { get; set; } = "DotNet";

    /// <summary>The host's DNS hostname.</summary>
    public required string DnsHostName { get; set; }

    /// <summary>The host's IP addresses.</summary>
    public required string[] IPAddresses { get; set; }

    /// <inheritdoc/>
    public override string ToString() =>
        $"{EdgeName} v{Version} (MachineName:{MachineName} DNS:{DnsHostName}) Platform: {Platform} ({OSVersion}) CLR: {CLRVersion}";
}
