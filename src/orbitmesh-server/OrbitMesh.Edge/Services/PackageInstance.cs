using System.Diagnostics;
using System.IO.Compression;
using OrbitMesh.Deployment;
using OrbitMesh.Edge.Configuration;
using OrbitMesh.Edge.Utils;
using Microsoft.Extensions.Logging;

namespace OrbitMesh.Edge.Services;

/// <summary>Owns the child process for one package: download/unzip, start/stop/restart, crash recovery, CPU/RAM sampling.</summary>
public sealed class PackageInstance(
    PackageDescription package,
    EdgeOptions options,
    string edgeName,
    int currentProcessId,
    HttpClient httpClient,
    ILogger logger,
    Action<PackageDescription, PackageState> onStateChanged,
    JobObject? jobObject)
{
    private Guid _processInstanceId = Guid.Empty;
    private PackageState _state = PackageState.Stopped;
    private int _startingCount;
    private DateTime _lastStart;

    public PackageDescription Package { get; private set; } = package;

    public Process? Process { get; private set; }

    public ProcessMonitor? Monitor { get; private set; }

    public PackageState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                onStateChanged(Package, _state);
            }
        }
    }

    public void UpdateDescription(PackageDescription package) => Package = package;

    // Package.Name and the manifest's ExecutableFilename both ultimately come from a Management-API
    // caller (adding a package instance, or the PackageInfo.xml inside an uploaded zip) - neither is
    // validated for path-traversal characters upstream, so a value like "../../etc" must not be able
    // to make this resolve (and then Directory.Delete, in DownloadPackageAsync) outside the intended
    // packages root. This is the containment check for that.
    private string InstanceDirectory => ResolveContained(options.LocalPackagesDirectory, Package.Name, "Package name");

    private static string ResolveContained(string baseDirectory, string relativePath, string what)
    {
        var root = Path.GetFullPath(baseDirectory);
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (candidate != root && !candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{what} '{relativePath}' would resolve outside '{root}' - rejected.");
        }
        return candidate;
    }

    private PackageManifest? Manifest => PackageManifestHelper.GetPackageManifestFromDirectory(InstanceDirectory, throwException: false);

    public async Task StartAsync()
    {
        if (_processInstanceId != Guid.Empty || await DownloadPackageAsync())
        {
            StartProcess();
        }
    }

    public Task Stop() => StopProcessAsync();

    public async Task RestartAsync()
    {
        await StopProcessAsync();
        await Task.Delay(1000);
        StartProcess();
    }

    public async Task ReloadAsync()
    {
        await StopProcessAsync();
        await Task.Delay(1000);
        if (await DownloadPackageAsync())
        {
            StartProcess();
        }
    }

    private void StartProcess()
    {
        if (State != PackageState.Stopped)
        {
            logger.LogError("The package '{Package}' is already running", Package.Name);
            return;
        }
        State = PackageState.Starting;

        var manifest = Manifest;
        if (manifest != null && !manifest.IsCompliantOnCurrentPlatform())
        {
            logger.LogError("The package '{Package}' is NOT compliant with your platform. Maybe unstable!", Package.Name);
        }
        if (manifest != null && manifest.Runtime != PackageRuntime.DotNet)
        {
            logger.LogError(
                "Unable to start the package '{Package}': it declares Runtime=\"{Runtime}\", but this is a .NET Edge - deploy it to a matching Edge instead.",
                Package.Name, manifest.Runtime);
            State = PackageState.Stopped;
            return;
        }

        var executable = manifest?.ExecutableFilename;
        if (string.IsNullOrEmpty(executable))
        {
            executable = !string.IsNullOrEmpty(manifest?.Name) ? $"{manifest.Name}.dll" : $"{Package.Name}.dll";
        }

        string executablePath;
        try
        {
            // Both Package.Name and manifest.ExecutableFilename come from the Management API upstream
            // (server-side, not necessarily validated there) - ResolveContained rejects any "../" or
            // absolute-path trick that would otherwise let a package escape LocalPackagesDirectory.
            executablePath = ResolveContained(InstanceDirectory, executable, "ExecutableFilename");
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Unable to start the package '{Package}': invalid executable path", Package.Name);
            State = PackageState.Stopped;
            return;
        }
        if (!File.Exists(executablePath))
        {
            logger.LogError("Unable to start the package: the process file ({Path}) was not found. Check the PackageManifest!", executablePath);
            State = PackageState.Stopped;
            return;
        }

        var accessKey = !string.IsNullOrEmpty(Package.AccessKey) ? Package.AccessKey : options.OrbitMeshAccessKey;

        var startInfo = executablePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? new ProcessStartInfo("dotnet") { ArgumentList = { executablePath } }
            : new ProcessStartInfo(executablePath);
        startInfo.ArgumentList.Add(options.OrbitMeshServerUri);
        startInfo.ArgumentList.Add(edgeName);
        startInfo.ArgumentList.Add(Package.Name);
        startInfo.ArgumentList.Add(accessKey);
        startInfo.ArgumentList.Add(currentProcessId.ToString());
        startInfo.CreateNoWindow = true;
        startInfo.UseShellExecute = false;
        startInfo.WorkingDirectory = Path.GetFullPath(InstanceDirectory);

        logger.LogTrace("Starting process '{FileName} {Arguments}'", startInfo.FileName, string.Join(' ', startInfo.ArgumentList));
        try
        {
            _processInstanceId = Guid.NewGuid();
            Process = System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while starting the process '{Executable}' (Package: {Package})", executable, Package.Name);
            State = PackageState.Stopped;
            return;
        }

        if (Process == null)
        {
            State = PackageState.Stopped;
            return;
        }

        // Ties this child's lifetime (and anything it in turn spawns) to the Edge process's - without
        // this, killing the Edge (crash, Task Manager "End task", any kill forceful enough that
        // StopAsync() never runs) leaves every package process running on its own, and the Edge has no
        // way to notice or reattach to them on its next start - it just launches fresh duplicates.
        if (OperatingSystem.IsWindows() && jobObject != null && !jobObject.AssignProcess(Process))
        {
            logger.LogWarning("Could not attach '{Package}' (PID {Pid}) to the Edge's job object - it may survive an unexpected Edge shutdown", Package.Name, Process.Id);
        }

        Process.EnableRaisingEvents = true;
        Process.Exited += (_, _) => OnProcessExited(executablePath);
        Monitor = new ProcessMonitor(Process);
        _lastStart = DateTime.Now;
        State = PackageState.Started;
        _startingCount++;
        logger.LogInformation("{Package} (version {Version}) started with PID {Pid} ({Path})", Package.Name, manifest?.Version ?? "?", Process.Id, executablePath);
    }

    private void OnProcessExited(string executablePath)
    {
        logger.LogWarning("'{Path}' has exited", executablePath);
        var wasStopping = State is PackageState.Stopped or PackageState.Stopping;
        State = PackageState.Stopped;
        if (wasStopping)
        {
            _startingCount = 0;
            return;
        }

        var recoveryOptions = Package.RecoveryOptions;
        if (recoveryOptions == null || !recoveryOptions.RestartAfterFailure)
        {
            logger.LogWarning("The package '{Package}' has exited but it will not be restarted", executablePath);
            _startingCount = 0;
            return;
        }
        if (DateTime.Now.Subtract(_lastStart).TotalMinutes >= recoveryOptions.ResetCounterAfterMinutes)
        {
            _startingCount = 0;
        }
        if (recoveryOptions.NumberOfRetry < _startingCount)
        {
            logger.LogWarning("The package '{Package}' exceeded its retry limit and will not be restarted", executablePath);
            _startingCount = 0;
            return;
        }

        var currentInstanceId = _processInstanceId;
        _ = Task.Run(async () =>
        {
            logger.LogInformation("Waiting {Seconds}s to restart '{Path}'", recoveryOptions.RestartPackageAfterSeconds, executablePath);
            await Task.Delay(TimeSpan.FromSeconds(recoveryOptions.RestartPackageAfterSeconds));
            if (State == PackageState.Stopped && _processInstanceId == currentInstanceId)
            {
                logger.LogInformation("Restarting '{Path}'", executablePath);
                StartProcess();
            }
        });
    }

    private async Task StopProcessAsync()
    {
        if (State == PackageState.Stopped || Process == null || Process.HasExited)
        {
            if (State != PackageState.Stopped)
            {
                State = PackageState.Stopped;
                logger.LogInformation("The package '{Package}' has been stopped", Package.Name);
            }
            return;
        }

        State = PackageState.Stopping;
        logger.LogInformation("Stopping '{Package}' with PID {Pid}", Package.Name, Process.Id);
        // Waits off-thread instead of Thread.Sleep-polling, freeing the thread pool worker this runs on
        // (Stop()/RestartAsync()/ReloadAsync() are all invoked fire-and-forget from EdgeManager).
        using var cts = new CancellationTokenSource(options.ShutdownPackageTimeoutMs);
        try
        {
            await Process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Timed out waiting for a graceful exit - fall through to force-kill below.
        }
        if (!Process.HasExited)
        {
            try
            {
                Process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                logger.LogTrace(ex, "Exception while killing the package '{Package}'", Package.Name);
            }
            State = PackageState.Stopped;
        }
    }

    private async Task<bool> DownloadPackageAsync()
    {
        try
        {
            var uri = $"{options.OrbitMeshServerUri.TrimEnd('/')}/packages/{Package.PackageFile}";
            var destination = InstanceDirectory;
            logger.LogTrace("DownloadPackage URI:{Uri} Path:{Path}", uri, destination);
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }
            Directory.CreateDirectory(destination);

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add(OrbitMeshHeaderNames.EdgeName, edgeName);
            request.Headers.Add(OrbitMeshHeaderNames.PackageName, "Edge");
            request.Headers.Add(OrbitMeshHeaderNames.AccessKey, options.OrbitMeshAccessKey);

            logger.LogInformation("Downloading {Package} (URI:{Uri})", Package.Name, uri);
            using var response = await httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var tempZip = Path.GetTempFileName();
            await using (var fileStream = File.Create(tempZip))
            {
                await response.Content.CopyToAsync(fileStream);
            }
            logger.LogInformation("Unzipping {Package}", Package.Name);
            ZipFile.ExtractToDirectory(tempZip, destination, overwriteFiles: true);
            File.Delete(tempZip);
            logger.LogTrace("Package installed");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to download package '{Package}'", Package.Name);
            return false;
        }
    }
}
