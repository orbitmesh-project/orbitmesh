using System.Diagnostics;
using System.IO.Compression;
using OrbitMesh.Edge.Configuration;
using OrbitMesh.Updating;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;

namespace OrbitMesh.Edge.Services;

/// <summary>Downloads, verifies and stages a OrbitMesh.Edge release, then hands off to
/// OrbitMesh.Updater and exits - mirrors OrbitMesh.Server.Services.ServerSelfUpdater; see its own doc
/// comment for why a separate process doing the swap works uniformly across OS/hosting mode.</summary>
public sealed class EdgeSelfUpdater(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<EdgeOptions> options,
    ReleaseVerifier verifier,
    ILogger<EdgeSelfUpdater> logger)
{
    public async Task ApplyAsync(string zipUrl, CancellationToken cancellationToken = default)
    {
        var liveDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var parent = Path.GetDirectoryName(liveDir)
            ?? throw new InvalidOperationException("Cannot determine the parent of the running application's directory.");
        var stagingDir = Path.Combine(parent, "__orbitmesh_update_staging__");

        if (Directory.Exists(stagingDir))
        {
            Directory.Delete(stagingDir, recursive: true);
        }
        Directory.CreateDirectory(stagingDir);

        var updateOptions = options.CurrentValue.UpdateOptions;
        var zipPath = await ReleaseZipDownloader.DownloadToTempFileAsync(httpClientFactory, nameof(EdgeSelfUpdater), zipUrl, updateOptions.Token, cancellationToken);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, stagingDir);

            if (!File.Exists(Path.Combine(stagingDir, "OrbitMesh.Edge.dll")))
            {
                throw new InvalidOperationException("Downloaded release does not contain OrbitMesh.Edge.dll - refusing to apply it.");
            }

            verifier.VerifyIfRequired(stagingDir, updateOptions.PublicKeys);

            PreserveLiveState(liveDir, stagingDir);
        }
        finally
        {
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }
        }

        var handoff = BuildHandoffArgs(liveDir, stagingDir, updateOptions);
        LaunchUpdater(updateOptions, handoff);

        logger.LogInformation("Handed off to OrbitMesh.Updater - it will wait for this process to exit, then swap the files and restart the Edge.");
        _ = Task.Delay(500).ContinueWith(_ => Environment.Exit(ExitCodes.UpdatePending), cancellationToken);
    }

    private static UpdateHandoffArgs BuildHandoffArgs(string liveDir, string stagingDir, UpdateOptions updateOptions)
    {
        var restartMode = WindowsServiceHelpers.IsWindowsService() ? RestartMode.WindowsService
            : SystemdHelpers.IsSystemdService() ? RestartMode.Systemd
            : RestartMode.Standalone;

        var (executable, arguments) = RelaunchCommand.Capture();

        return new UpdateHandoffArgs
        {
            CallerPid = Environment.ProcessId,
            LiveDirectory = liveDir,
            StagingDirectory = stagingDir,
            RestartMode = restartMode,
            ServiceOrUnitName = updateOptions.ServiceOrUnitName,
            RestartExecutable = executable,
            RestartArguments = arguments,
            RestartWorkingDirectory = liveDir,
            // The Edge has no HTTP surface of its own to poll for a health check (unlike the Server's
            // /rest/management/server/version) - it reports its own health by reconnecting to the
            // Server's SignalR hub, which Updater has no way to observe. Skipping the check means a
            // broken Edge release won't auto-rollback; it will however still show as disconnected in
            // the console immediately, the same way a crash-looping Edge would today.
            HealthCheckUrl = null
        };
    }

    private static void LaunchUpdater(UpdateOptions updateOptions, UpdateHandoffArgs handoff)
    {
        var startInfo = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        startInfo.ArgumentList.Add(ResolveUpdaterPath(updateOptions));
        foreach (var arg in handoff.ToArgv())
        {
            startInfo.ArgumentList.Add(arg);
        }
        Process.Start(startInfo);
    }

    private static string ResolveUpdaterPath(UpdateOptions updateOptions)
    {
        if (!string.IsNullOrEmpty(updateOptions.UpdaterExecutablePath))
        {
            return updateOptions.UpdaterExecutablePath;
        }
        var liveDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var parent = Path.GetDirectoryName(liveDir)
            ?? throw new InvalidOperationException("Cannot determine the parent of the running application's directory.");
        return Path.Combine(parent, "updater", "OrbitMesh.Updater.dll");
    }

    // This install's own appsettings.json (server URI, access key, edge name) and its locally cached
    // package downloads both live on disk next to the app's own files - a naive whole-directory swap
    // would silently wipe them out with whatever (or nothing) the release happens to ship in their
    // place. Mirrors ServerSelfUpdater.PreserveLiveState.
    private void PreserveLiveState(string liveDir, string stagingDir)
    {
        foreach (var fileName in new[] { "appsettings.json", "appsettings.json.bak" })
        {
            var source = Path.Combine(liveDir, fileName);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(stagingDir, fileName), overwrite: true);
            }
        }

        var packagesDir = Path.GetFullPath(options.CurrentValue.LocalPackagesDirectory, liveDir + Path.DirectorySeparatorChar);
        if (Directory.Exists(packagesDir) && IsWithin(liveDir, packagesDir))
        {
            var relative = Path.GetRelativePath(liveDir, packagesDir);
            var destination = Path.Combine(stagingDir, relative);
            var destinationParent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destinationParent))
            {
                Directory.CreateDirectory(destinationParent);
            }
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }
            Directory.Move(packagesDir, destination);
        }
    }

    private static bool IsWithin(string parentDir, string candidate) =>
        candidate.StartsWith(parentDir + Path.DirectorySeparatorChar, StringComparison.Ordinal);
}
