using System.Diagnostics;
using System.IO.Compression;
using OrbitMesh.Server.Configuration;
using OrbitMesh.Updating;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;

namespace OrbitMesh.Server.Services;

/// <summary>Downloads, verifies and stages a OrbitMesh.Server release, then hands off to
/// OrbitMesh.Updater and exits - Updater is what actually swaps the files (once this process is
/// confirmed fully gone) and restarts the Server. Works identically on Windows and Linux, and
/// regardless of hosting mode (standalone/Windows Service/systemd) - unlike the in-process swap this
/// used to do, which only ever worked on Linux (see git history for that approach and why it couldn't
/// support Windows).</summary>
public sealed class ServerSelfUpdater(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<OrbitMeshOptions> options,
    ReleaseVerifier verifier,
    ILogger<ServerSelfUpdater> logger)
{
    public async Task ApplyAsync(string zipUrl, CancellationToken cancellationToken = default)
    {
        var liveDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var parent = Path.GetDirectoryName(liveDir)
            ?? throw new InvalidOperationException("Cannot determine the parent of the running application's directory.");
        // Sibling of liveDir, so the eventual move Updater performs is a same-filesystem rename rather
        // than a cross-device copy - staging elsewhere (e.g. the OS temp dir, which may be a separate
        // mount) would silently turn the "atomic" swap into a slow, interruptible copy.
        var stagingDir = Path.Combine(parent, "__orbitmesh_update_staging__");

        if (Directory.Exists(stagingDir))
        {
            Directory.Delete(stagingDir, recursive: true);
        }
        Directory.CreateDirectory(stagingDir);

        var updateOptions = options.CurrentValue.UpdateOptions;
        var zipPath = await ReleaseZipDownloader.DownloadToTempFileAsync(httpClientFactory, nameof(ServerSelfUpdater), zipUrl, updateOptions.Token, cancellationToken);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, stagingDir);

            if (!File.Exists(Path.Combine(stagingDir, "OrbitMesh.Server.dll")))
            {
                throw new InvalidOperationException("Downloaded release does not contain OrbitMesh.Server.dll - refusing to apply it.");
            }

            // Before PreserveLiveState adds appsettings.json/packages/etc. below - those belong to
            // this install, not the release, and would otherwise show up as "unexpected files" against
            // a manifest that only ever describes the release payload itself.
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

        logger.LogInformation("Handed off to OrbitMesh.Updater - it will wait for this process to exit, then swap the files and restart the server.");
        // Gives the caller (ManagementController) a moment to flush its HTTP response before exiting.
        // ExitCodes.UpdatePending is a clean exit(0) on purpose - see its own doc comment for why that
        // matters to both systemd's Restart=on-failure and the Windows Service failure-actions flag.
        _ = Task.Delay(500).ContinueWith(_ => Environment.Exit(ExitCodes.UpdatePending), cancellationToken);
    }

    private UpdateHandoffArgs BuildHandoffArgs(string liveDir, string stagingDir, UpdateOptions updateOptions)
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
            HealthCheckUrl = BuildHealthCheckUrl(),
            HealthCheckTimeoutSeconds = 30
        };
    }

    private string BuildHealthCheckUrl()
    {
        var listenUrl = options.CurrentValue.ListenUrls.FirstOrDefault() ?? "http://localhost:8088";
        // "http://+:8088" (or "0.0.0.0") binds every interface but isn't itself a valid address to
        // connect *to* - substitute localhost, since the health check always runs on the same machine.
        var uri = new Uri(listenUrl.Replace("://+", "://localhost", StringComparison.Ordinal));
        var host = uri.Host is "0.0.0.0" ? "localhost" : uri.Host;
        return $"{uri.Scheme}://{host}:{uri.Port}/rest/management/server/version";
    }

    private static void LaunchUpdater(UpdateOptions updateOptions, UpdateHandoffArgs handoff)
    {
        // Environment.ProcessPath, not a bare "dotnet" - PATH isn't guaranteed to resolve it (systemd
        // doesn't set one), and a doomed Process.Start("dotnet") fails silently on Linux.
        var dotnetPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to determine the current process's executable path.");
        var startInfo = new ProcessStartInfo(dotnetPath) { UseShellExecute = false };
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
        // Deployed once, as a sibling of the live directory - it must live outside whatever this (or any
        // future) update swaps out, or updating would try to replace files its own running process
        // still has open.
        var liveDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var parent = Path.GetDirectoryName(liveDir)
            ?? throw new InvalidOperationException("Cannot determine the parent of the running application's directory.");
        return Path.Combine(parent, "updater", "OrbitMesh.Updater.dll");
    }

    // A release .zip is just this project's own build output - it never contains this specific
    // install's appsettings.json (edges, credentials, access keys, ...) nor the package .zips
    // that Edges have downloaded into PackagesRootDirectory. Both live on disk next to the app's
    // own files, so a naive whole-directory swap would silently wipe them out with whatever (or
    // nothing) the release happens to ship in their place. Carrying them into the staged directory
    // before the swap is what makes the swap safe to do unattended.
    private void PreserveLiveState(string liveDir, string stagingDir)
    {
        foreach (var fileName in new[] { "appsettings.json", "appsettings.json.bak", "TelemetryItemsSnapshot.json" })
        {
            var source = Path.Combine(liveDir, fileName);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(stagingDir, fileName), overwrite: true);
            }
        }

        // AccessKeyCipher's key - losing this silently bricks every Machine credential's decryption.
        var keysDir = Path.Combine(liveDir, "keys");
        if (Directory.Exists(keysDir))
        {
            var destinationKeysDir = Path.Combine(stagingDir, "keys");
            Directory.CreateDirectory(destinationKeysDir);
            foreach (var file in Directory.GetFiles(keysDir))
            {
                File.Copy(file, Path.Combine(destinationKeysDir, Path.GetFileName(file)), overwrite: true);
            }
        }

        var packagesDir = Path.GetFullPath(options.CurrentValue.PackagesRootDirectory, liveDir + Path.DirectorySeparatorChar);
        if (Directory.Exists(packagesDir) && IsWithin(liveDir, packagesDir))
        {
            var relative = Path.GetRelativePath(liveDir, packagesDir);
            var destination = Path.Combine(stagingDir, relative);
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }
            // Copy, not move: liveDir must still have its real packages/ if anything between here and
            // a successful swap goes wrong (this OS process killed, the Updater killed before it can
            // swap, ...) - a retry's "wipe any leftover staging dir" start-over would otherwise take
            // the only copy down with it.
            CopyDirectoryRecursive(packagesDir, destination);
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectoryRecursive(subDir, Path.Combine(destinationDir, Path.GetFileName(subDir)));
        }
    }

    private static bool IsWithin(string parentDir, string candidate) =>
        candidate.StartsWith(parentDir + Path.DirectorySeparatorChar, StringComparison.Ordinal);
}
