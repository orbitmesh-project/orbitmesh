using System.IO.Compression;
using OrbitMesh.Deployment;

namespace OrbitMesh.Server.Services;

/// <summary>Reads package manifests out of package zip files using the native System.IO.Compression (no SharpZipLib).</summary>
public static class PackageZipHelper
{
    public static PackageManifest? GetManifest(string zipFilePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipFilePath);
            var entry = archive.GetEntry(PackageManifestHelper.PackageManifestFilename);
            if (entry == null)
            {
                return null;
            }
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            return PackageManifestHelper.GetPackageManifestFromXml(reader.ReadToEnd(), throwException: false);
        }
        catch
        {
            return null;
        }
    }

    public static byte[]? GetEntryContent(string zipFilePath, string entryName)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipFilePath);
            var entry = archive.GetEntry(entryName);
            if (entry == null)
            {
                return null;
            }
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
