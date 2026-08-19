using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using OrbitMesh.Deployment;
using OrbitMesh.Edge.Configuration;
using OrbitMesh.Edge.Utils;
using OrbitMesh.Updating;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OrbitMesh.Edge.Services;

public sealed record PackageControlActionMessage(string PackageName, PackageControlAction Action);

public sealed record PackageUsageReport(string PackageName, double CpuUsage, long RamUsage);

/// <summary>Connects to the OrbitMesh server, receives the package list, and supervises package processes.</summary>
public sealed class EdgeManager(IOptions<EdgeOptions> optionsAccessor, ILogger<EdgeManager> logger, IHttpClientFactory httpClientFactory, EdgeUpdateCheckService updateCheckService) : IAsyncDisposable
{
    private readonly EdgeOptions _options = optionsAccessor.Value;
    private readonly Dictionary<string, PackageInstance> _packages = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly (string Executable, string[] Arguments) _relaunchCommand = RelaunchCommand.Capture();
    // NOT AppContext.BaseDirectory: RelaunchCommand.Arguments carries the dll path exactly as it
    // appeared on the ORIGINAL command line, which is only guaranteed to resolve correctly relative to
    // wherever the process was actually launched FROM (Environment.CurrentDirectory) - if that ever
    // differs from the folder the dll itself lives in (e.g. launched as "dotnet bin/.../Edge.dll" from
    // one directory up), AppContext.BaseDirectory would double up that relative path into a location
    // that doesn't exist, and the replacement process would fail to start silently.
    private readonly string _workingDirectory = Environment.CurrentDirectory;
    private HubConnection? _connection;
    private EdgeDescription _description = null!;
    private string _edgeName = string.Empty;
    private CancellationTokenSource? _cts;
    private int _restartRequested;
    // Windows-only (see JobObject's own doc comment for why this is the real fix for orphaned package
    // processes) - null on other platforms, in which case PackageInstance just skips the assignment.
    private JobObject? _jobObject;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _edgeName = string.IsNullOrEmpty(_options.EdgeName) ? Environment.MachineName : _options.EdgeName;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var instanceId = InstanceIdProvider.GetOrCreate(_workingDirectory);

        Directory.CreateDirectory(_options.LocalPackagesDirectory);

        if (OperatingSystem.IsWindows())
        {
            try
            {
                _jobObject = new JobObject();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unable to create the Edge's job object - package processes won't be auto-killed if the Edge exits unexpectedly");
            }
        }

        var addresses = Array.Empty<string>();
        try
        {
            addresses = (await Dns.GetHostEntryAsync(Dns.GetHostName(), cancellationToken)).AddressList.Select(ip => ip.ToString()).ToArray();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Unable to resolve local IP addresses");
        }

        _description = new EdgeDescription
        {
            EdgeName = _edgeName,
            MachineName = Environment.MachineName,
            OSVersion = Environment.OSVersion.ToString(),
            OSCaption = RuntimeInformation.OSDescription,
            Platform = Environment.OSVersion.Platform.ToString(),
            CLRVersion = Environment.Version.ToString(),
            CLRImplementation = "Microsoft",
            FxVersion = RuntimeInformation.FrameworkDescription,
            DnsHostName = Dns.GetHostName(),
            IPAddresses = addresses,
            Version = typeof(EdgeManager).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Runtime = nameof(PackageRuntime.DotNet),
            InstanceId = instanceId
        };

        _connection = new HubConnectionBuilder()
            .WithUrl($"{_options.OrbitMeshServerUri.TrimEnd('/')}/signalr/hubs/{OrbitMeshHubNames.Edge}", o =>
            {
                o.Headers.Add(OrbitMeshHeaderNames.EdgeName, _edgeName);
                o.Headers.Add(OrbitMeshHeaderNames.PackageName, OrbitMeshDefaultNames.EdgePackageName);
                o.Headers.Add(OrbitMeshHeaderNames.AccessKey, _options.OrbitMeshAccessKey);
                o.Headers.Add(OrbitMeshHeaderNames.InstanceId, instanceId);
            })
            .WithOrbitMeshDefaults()
            .Build();

        _connection.On<List<PackageDescription>>(EdgeClientMethodNames.PushPackagesList, OnPackagesListReceived);
        _connection.On<PackageControlActionMessage>(EdgeClientMethodNames.PackageControlAction, m => OnControlAction(m.Action, m.PackageName));
        _connection.On(EdgeServerMethodNames.RestartEdge, () => _ = RestartSelfAsync());
        _connection.On(EdgeServerMethodNames.CheckForUpdate, () => _ = CheckForUpdateNowAsync());
        _connection.On<string>(EdgeClientMethodNames.EdgeApproved, OnApproved);
        _connection.Reconnected += _ => RegisterAsync();
        _connection.Reconnecting += ex =>
        {
            logger.LogWarning(ex, "Disconnected from the hub! Trying to reconnect...");
            return Task.CompletedTask;
        };

        await _connection.StartAsync(cancellationToken);
        await RegisterAsync();

        NamedPipeHelper.StartServer(NamedPipeHelper.GetCurrentProcessPipeName(), OnNamedPipeMessage, ex => logger.LogError(ex, "Named pipe server error"), _cts.Token);

        _ = ReportUsageLoopAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        // Belt-and-suspenders alongside the job object: on a graceful shutdown this stops packages
        // cleanly (proper "Stopped" state, respecting ShutdownPackageTimeoutMs) rather than waiting on
        // the OS to tear the job down. The job object is what still catches the case this can't - a
        // kill forceful enough that this method never runs at all.
        List<PackageInstance> instances;
        lock (_lock)
        {
            instances = _packages.Values.ToList();
        }
        await Task.WhenAll(instances.Select(i => i.Stop()));
        if (_connection != null)
        {
            await _connection.StopAsync();
        }
        if (OperatingSystem.IsWindows())
        {
            _jobObject?.Dispose();
        }
    }

    public static bool SendControlActionToRunningInstance(string action, string packageName) =>
        NamedPipeHelper.TrySendMessage(NamedPipeHelper.GetCurrentProcessPipeName(), $"{action} {packageName}");

    // Fired when the Console's "check for update" button pushes EdgeServerMethodNames.CheckForUpdate.
    private async Task CheckForUpdateNowAsync()
    {
        try
        {
            await updateCheckService.CheckAndApplyAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "On-demand update check failed");
        }
    }

    // Fired when an admin approves this Edge from the Console's pending-edges list.
    private void OnApproved(string accessKey)
    {
        try
        {
            ApplyApprovedAccessKey(accessKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Received an approved AccessKey but couldn't save it to appsettings.json - apply it by hand instead.");
            return;
        }
        logger.LogInformation("Approved by the server - restarting to connect with the new AccessKey.");
        // _options only reflects appsettings.json at startup - restart to pick up the change just written.
        _ = RestartSelfAsync();
    }

    private void ApplyApprovedAccessKey(string accessKey)
    {
        var path = Path.Combine(_workingDirectory, "appsettings.json");
        var root = JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"{path} is empty or not valid JSON.");
        if (root["Edge"] is not JsonObject edgeSection)
        {
            throw new InvalidOperationException($"{path} has no \"Edge\" section to update.");
        }
        edgeSection["OrbitMeshAccessKey"] = accessKey;
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private Task RestartSelfAsync()
    {
        if (Interlocked.Exchange(ref _restartRequested, 1) != 0)
        {
            return Task.CompletedTask;
        }

        return Task.Run(async () =>
        {
            try
            {
                // Stop packages (and this connection) BEFORE spawning the replacement - the new
                // process is going to redownload and extract these same packages into these same
                // Packages/<name> directories, which fails silently (DownloadPackageAsync catches and
                // just logs) as long as these still-running instances hold their own files open.
                await StopAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unable to restart the Edge");
            }

            if (!TryStartReplacementProcess())
            {
                Interlocked.Exchange(ref _restartRequested, 0);
                return;
            }
            Environment.Exit(ExitCodes.UpdatePending);
        });
    }

    private bool TryStartReplacementProcess()
    {
        try
        {
            // Under a service manager, spawning the replacement directly would leave it untracked -
            // wait for this PID to exit, then let the service manager start the unit itself.
            if (WindowsServiceHelpers.IsWindowsService())
            {
                return TryStartServiceManagerWatcher("powershell.exe",
                    "-NoProfile", "-Command",
                    "param($ProcessId,$ServiceName) while (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 500 }; sc.exe start $ServiceName",
                    "-ProcessId", Environment.ProcessId.ToString(), "-ServiceName", RequireServiceOrUnitName());
            }
            if (SystemdHelpers.IsSystemdService())
            {
                return TryStartServiceManagerWatcher("bash", "-c",
                    "while kill -0 \"$1\" 2>/dev/null; do sleep 0.5; done; sudo systemctl start \"$2\"",
                    "bash", Environment.ProcessId.ToString(), RequireServiceOrUnitName());
            }

            var startInfo = new ProcessStartInfo(_relaunchCommand.Executable)
            {
                UseShellExecute = false,
                // Without this, the child tries to allocate/attach its own console - fine for a
                // process launched from an interactive terminal, but the Edge itself is typically
                // started detached/windowless (a service, or launched hidden), so that attempt has
                // nothing to attach to and the replacement process hangs before ever reaching its own
                // Main() logging. PackageInstance.StartProcess() already sets this for the same reason.
                CreateNoWindow = true,
                WorkingDirectory = _workingDirectory
            };
            foreach (var arg in _relaunchCommand.Arguments)
            {
                startInfo.ArgumentList.Add(arg);
            }
            Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to launch a replacement Edge process");
            return false;
        }
    }

    private string RequireServiceOrUnitName() =>
        _options.UpdateOptions.ServiceOrUnitName ?? throw new InvalidOperationException("UpdateOptions.ServiceOrUnitName must be set to restart under a service manager.");

    // args are passed as real argv entries (not interpolated into the script string), so a
    // service/unit name never needs shell-escaping.
    private static bool TryStartServiceManagerWatcher(string executable, params string[] args)
    {
        var startInfo = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }
        Process.Start(startInfo);
        return true;
    }

    private async Task RegisterAsync()
    {
        try
        {
            var registered = await _connection!.InvokeAsync<bool>(EdgeServerMethodNames.RegisterEdge, _description);
            if (!registered)
            {
                logger.LogWarning("Edge registration failed, retrying in 3 seconds...");
                await Task.Delay(3000);
                await RegisterAsync();
                return;
            }
            logger.LogInformation("Edge '{Name}' registered", _edgeName);
            lock (_lock)
            {
                foreach (var instance in _packages.Values)
                {
                    _ = _connection!.InvokeAsync(EdgeServerMethodNames.ReportPackageState, instance.Package, instance.State);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to register the edge");
        }
    }

    private void OnPackagesListReceived(List<PackageDescription> packages)
    {
        logger.LogInformation("Received {Count} package(s) from the OrbitMesh server", packages.Count);
        var toStart = new List<PackageInstance>();

        lock (_lock)
        {
            foreach (var package in packages)
            {
                if (string.IsNullOrEmpty(package.PackageFile))
                {
                    continue;
                }
                if (_packages.TryGetValue(package.Name, out var existing))
                {
                    existing.UpdateDescription(package);
                }
                else
                {
                    var instance = new PackageInstance(package, _options, _edgeName, Environment.ProcessId, httpClientFactory.CreateClient(nameof(EdgeManager)), logger, ReportPackageState, _jobObject);
                    _packages[package.Name] = instance;
                    if (package.AutoStart)
                    {
                        toStart.Add(instance);
                    }
                }
            }

            var removed = _packages.Keys.Where(name => !packages.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).ToList();
            foreach (var name in removed)
            {
                var instance = _packages[name];
                _packages.Remove(name);
                logger.LogInformation("Removing '{Package}'", name);
                _ = instance.Stop();
            }
        }

        foreach (var instance in toStart)
        {
            _ = instance.StartAsync();
        }
    }

    private void OnControlAction(PackageControlAction action, string packageName)
    {
        PackageInstance? instance;
        lock (_lock)
        {
            _packages.TryGetValue(packageName, out instance);
        }
        if (instance == null)
        {
            logger.LogError("Unable to {Action} '{Package}': this package doesn't exist", action, packageName);
            return;
        }
        _ = action switch
        {
            PackageControlAction.Start => instance.StartAsync(),
            PackageControlAction.Restart => instance.RestartAsync(),
            PackageControlAction.Stop => instance.Stop(),
            PackageControlAction.Reload => instance.ReloadAsync(),
            _ => Task.CompletedTask
        };
    }

    private void OnNamedPipeMessage(string message)
    {
        var parts = message.Split(' ', 2);
        if (parts.Length == 2 && Enum.TryParse<PackageControlAction>(parts[0], out var action))
        {
            OnControlAction(action, parts[1]);
        }
    }

    private void ReportPackageState(PackageDescription package, PackageState state)
    {
        if (_connection is { State: HubConnectionState.Connected })
        {
            _ = _connection.InvokeAsync(EdgeServerMethodNames.ReportPackageState, package, state);
        }
    }

    private async Task ReportUsageLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_connection is { State: HubConnectionState.Connected })
                {
                    List<PackageInstance> snapshot;
                    lock (_lock)
                    {
                        snapshot = _packages.Values.Where(p => p.Process is { HasExited: false } && p.Monitor != null).ToList();
                    }
                    if (snapshot.Count > 0)
                    {
                        var report = snapshot.Select(p => new PackageUsageReport(p.Package.Name, p.Monitor!.GetCpuUsagePercent(), p.Monitor.GetWorkingSet())).ToList();
                        await _connection.InvokeAsync(EdgeServerMethodNames.ReportPackagesUsage, report, cancellationToken);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogTrace(ex, "Error while reporting package usage");
            }
            try
            {
                await Task.Delay(_options.ReportPackageUsageIntervalMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        if (_connection != null)
        {
            await _connection.DisposeAsync();
        }
    }
}
