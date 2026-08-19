using System.IO.Compression;
using OrbitMesh.Server.Configuration;
using OrbitMesh.Updating;
using Microsoft.Extensions.Options;
using FileServerOptions = OrbitMesh.Server.Configuration.FileServerOptions;

namespace OrbitMesh.Server.Services;

/// <summary>Downloads and applies an update to a static site (Console, or any other) served from a
/// <c>FileServerOptions</c> entry. Unlike <see cref="ServerSelfUpdater"/> this never needs a process
/// restart - it's just files that Kestrel's static file middleware reads fresh on the next request -
/// so it works the same way regardless of the Server's own OS.</summary>
public sealed class StaticSiteUpdater(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<OrbitMeshOptions> options,
    ReleaseVerifier verifier,
    ILogger<StaticSiteUpdater> logger)
{
    private const string VersionMarkerFileName = ".orbitmesh-release-version";

    public static string? ReadCurrentVersion(string physicalPath)
    {
        var path = Path.Combine(Path.GetFullPath(physicalPath), VersionMarkerFileName);
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    public async Task ApplyAsync(FileServerOptions site, string version, string zipUrl, CancellationToken cancellationToken = default)
    {
        var liveDir = Path.GetFullPath(site.PhysicalPath).TrimEnd(Path.DirectorySeparatorChar);
        var parent = Path.GetDirectoryName(liveDir)
            ?? throw new InvalidOperationException($"Cannot determine the parent of '{liveDir}'.");
        var siteName = Path.GetFileName(liveDir);
        // Suffixed with the site's own folder name so updates to different sites never collide if
        // ever applied concurrently.
        var stagingDir = Path.Combine(parent, $"__orbitmesh_update_staging_{siteName}__");
        var backupDir = Path.Combine(parent, $"__orbitmesh_update_backup_{siteName}__");

        if (Directory.Exists(stagingDir))
        {
            Directory.Delete(stagingDir, recursive: true);
        }
        Directory.CreateDirectory(stagingDir);

        var updateOptions = options.CurrentValue.UpdateOptions;
        var zipPath = await ReleaseZipDownloader.DownloadToTempFileAsync(httpClientFactory, nameof(StaticSiteUpdater), zipUrl, updateOptions.Token, cancellationToken);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, stagingDir);

            // Before PreserveFiles/the version marker below add files of their own - those belong to
            // this install, not the release, and would otherwise show up as "unexpected files" against
            // a manifest that only ever describes the release payload itself.
            verifier.VerifyIfRequired(stagingDir, updateOptions.PublicKeys);

            // Per-install files a release build can't know in advance - e.g. a device-specific
            // config.json holding that site's own server URL/access key. Carried forward from the live
            // directory so the swap below never wipes them out with whatever (or nothing) the
            // release happens to ship in their place.
            if (Directory.Exists(liveDir))
            {
                foreach (var fileName in site.PreserveFiles)
                {
                    var source = Path.Combine(liveDir, fileName);
                    if (File.Exists(source))
                    {
                        File.Copy(source, Path.Combine(stagingDir, fileName), overwrite: true);
                    }
                }
            }

            // Written last, after any preserved files, so a stray marker file inside the release
            // zip itself (e.g. from a previous mistaken build) can never win over the version this
            // apply actually just installed.
            await File.WriteAllTextAsync(Path.Combine(stagingDir, VersionMarkerFileName), version, cancellationToken);

            if (Directory.Exists(backupDir))
            {
                Directory.Delete(backupDir, recursive: true);
            }
            var liveDirExisted = Directory.Exists(liveDir);
            if (liveDirExisted)
            {
                Directory.Move(liveDir, backupDir);
            }
            try
            {
                Directory.Move(stagingDir, liveDir);
            }
            catch
            {
                // The site would otherwise be left with no live directory at all (a 404, not just a
                // stale version) if this second rename fails after the first one already succeeded -
                // put the previous version straight back rather than leave that half-done.
                if (liveDirExisted && Directory.Exists(backupDir) && !Directory.Exists(liveDir))
                {
                    Directory.Move(backupDir, liveDir);
                }
                throw;
            }

            logger.LogInformation("Applied {Version} to static site '{Path}'.", version, site.Path);
        }
        finally
        {
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }
        }
    }
}
