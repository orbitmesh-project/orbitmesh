namespace OrbitMesh.Deployment;

/// <summary>Which Edge runtime a package's <see cref="PackageManifest.ExecutableFilename"/> needs to
/// be launched - a .NET Edge only knows how to run a "dotnet" package (or a native executable), a
/// Python Edge only a "python" one. Declared explicitly rather than inferred from the executable's file
/// extension, since that's not reliable (e.g. a Python package built into a standalone binary via
/// PyInstaller has no ".py" extension at all) and doesn't let an Edge refuse a mismatched package with
/// a clear error before even trying to launch it.</summary>
public enum PackageRuntime
{
    /// <summary>Launched via <c>dotnet &lt;path&gt;.dll</c> (or the executable directly) by
    /// OrbitMesh.Edge (.NET).</summary>
    DotNet,

    /// <summary>Launched via the Python interpreter (or the executable directly) by orbitmesh-edge
    /// (Python).</summary>
    Python
}
