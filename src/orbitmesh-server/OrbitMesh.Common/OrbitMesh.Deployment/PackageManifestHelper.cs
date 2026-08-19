using System.Xml.Serialization;

namespace OrbitMesh.Deployment;

/// <summary>Reads a <see cref="PackageManifest"/> from a <c>PackageInfo.xml</c> file, directory, or raw
/// XML string.</summary>
public static class PackageManifestHelper
{
    /// <summary>The manifest's standard filename, "PackageInfo.xml".</summary>
    public const string PackageManifestFilename = "PackageInfo.xml";

    private static readonly XmlSerializer Serializer = new(typeof(PackageManifest));

    private static PackageManifest? _currentPackageManifest;
    private static bool _currentPackageManifestLoaded;

    /// <summary>The manifest for the currently running package, read from its own base directory.</summary>
    public static PackageManifest? CurrentPackageManifest
    {
        get
        {
            if (!_currentPackageManifestLoaded)
            {
                _currentPackageManifest = GetPackageManifestFromDirectory(AppContext.BaseDirectory, throwException: false);
                _currentPackageManifestLoaded = true;
            }
            return _currentPackageManifest;
        }
    }

    /// <summary>True if <paramref name="path"/> contains a <see cref="PackageManifestFilename"/> file.</summary>
    public static bool ExistPackageManifest(string path) => File.Exists(Path.Combine(path, PackageManifestFilename));

    /// <summary>Reads and parses the manifest from <paramref name="path"/>'s <see cref="PackageManifestFilename"/>
    /// file, or returns null (or throws, if <paramref name="throwException"/>) if it doesn't exist/is invalid.</summary>
    public static PackageManifest? GetPackageManifestFromDirectory(string path, bool throwException = true)
    {
        if (!ExistPackageManifest(path))
        {
            return null;
        }
        return GetPackageManifestFromXml(File.ReadAllText(Path.Combine(path, PackageManifestFilename)), throwException);
    }

    /// <summary>Parses a manifest from a raw XML string, or returns null (or throws, if
    /// <paramref name="throwException"/>) if it's invalid.</summary>
    public static PackageManifest? GetPackageManifestFromXml(string xml, bool throwException = true)
    {
        try
        {
            using var reader = new StringReader(xml);
            return (PackageManifest?)Serializer.Deserialize(reader);
        }
        catch
        {
            if (throwException)
            {
                throw;
            }
            return null;
        }
    }
}
