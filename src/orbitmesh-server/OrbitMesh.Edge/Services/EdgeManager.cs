using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
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
    private string _instanceId = string.Empty;
    private CancellationTokenSource? _cts;
    private int _restartRequested;
    // Windows-only (see JobObject's own doc comment for why this is the real fix for orphaned package
    // processes) - null on other platforms, in which case PackageInstance just skips the assignment.
    private JobObject? _jobObject;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _edgeName = string.IsNullOrEmpty(_options.EdgeName) ? Environment.MachineName : _options.EdgeName;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _instanceId = InstanceIdProvider.GetOrCreate(_workingDirectory);
        CheckTransportSecurity();

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
            InstanceId = _instanceId
        };

        _connection = new HubConnectionBuilder()
            .WithUrl($"{_options.OrbitMeshServerUri.TrimEnd('/')}/signalr/hubs/{OrbitMeshHubNames.Edge}", o =>
            {
                o.Headers.Add(OrbitMeshHeaderNames.EdgeName, _edgeName);
                o.Headers.Add(OrbitMeshHeaderNames.PackageName, OrbitMeshDefaultNames.EdgePackageName);
                o.Headers.Add(OrbitMeshHeaderNames.AccessKey, _options.OrbitMeshAccessKey);
                o.Headers.Add(OrbitMeshHeaderNames.InstanceId, _instanceId);
            })
            .WithOrbitMeshDefaults()
            .Build();

        _connection.On<List<PackageDescription>>(EdgeClientMethodNames.PushPackagesList, OnPackagesListReceived);
        _connection.On<PackageControlActionMessage>(EdgeClientMethodNames.PackageControlAction, m => OnControlAction(m.Action, m.PackageName));
        _connection.On(EdgeServerMethodNames.RestartEdge, () => _ = RestartSelfAsync());
        _connection.On(EdgeServerMethodNames.CheckForUpdate, () => _ = CheckForUpdateNowAsync());
        _connection.Reconnected += _ => RegisterAsync();
        _connection.Reconnecting += ex =>
        {
            logger.LogWarning(ex, "Disconnected from the hub! Trying to reconnect...");
            return Task.CompletedTask;
        };

        if (!await TryConnectAsync(cancellationToken))
        {
            // Not yet approved (or the server/network is briefly unreachable). The Server aborts an
            // unauthorized connection right after the handshake, so StartAsync itself usually succeeds
            // and it's the first call (RegisterEdge) that fails - which is why this keys off
            // TryConnectAsync's result rather than an exception. Poll rest/enroll instead, forever; it
            // falls back to retrying the SignalR connection if the server says it already knows us.
            logger.LogWarning("Not connected to the OrbitMesh server - falling back to REST enrollment (not yet approved, or the server is unreachable).");
            _ = EnrollAndPollAsync(_cts.Token);
        }

        NamedPipeHelper.StartServer(NamedPipeHelper.GetCurrentProcessPipeName(), OnNamedPipeMessage, ex => logger.LogError(ex, "Named pipe server error"), _cts.Token);

        _ = ReportUsageLoopAsync(_cts.Token);
    }

    // The AccessKey (and, during enrollment, the freshly-approved one) travels in request headers, so
    // plain HTTP to anything but this machine hands it to whoever is on the path. Warns by default - a
    // LAN install over http:// is a legitimate (if weaker) setup that an update must not silently break -
    // and refuses to start when Edge:RequireHttps is set.
    private void CheckTransportSecurity()
    {
        if (!Uri.TryCreate(_options.OrbitMeshServerUri, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || uri.IsLoopback)
        {
            return;
        }
        if (_options.RequireHttps)
        {
            throw new InvalidOperationException($"Edge:RequireHttps is set but Edge:OrbitMeshServerUri is plain HTTP to a non-local host ({uri.Host}). Use an https:// URL.");
        }
        logger.LogWarning("Edge:OrbitMeshServerUri uses plain HTTP to {Host}: the AccessKey is sent unencrypted. Serve the Server over HTTPS (or behind a TLS reverse proxy) and set Edge:RequireHttps to enforce it.", uri.Host);
    }

    // True once connected AND registered. On failure the connection is stopped so it can't sit in
    // WithOrbitMeshDefaults()'s endless reconnect loop (each attempt would be aborted again by the
    // Server) while the REST enrollment path takes over.
    private async Task<bool> TryConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _connection!.StartAsync(cancellationToken);
            if (await RegisterAsync())
            {
                return true;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Unable to connect to the OrbitMesh server");
        }

        try
        {
            await _connection!.StopAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error while stopping the connection after a failed connect");
        }
        return false;
    }

    private async Task EnrollAndPollAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(nameof(EdgeManager));
        var baseUri = _options.OrbitMeshServerUri.TrimEnd('/');
        var request = new EnrollRequest(_description.EdgeName, _description.InstanceId, _description.MachineName, _description.OSVersion, _description.OSCaption, _description.Platform);
        var secret = EnrollSecretProvider.GetOrCreate(_workingDirectory);
        var enrolled = false;
        string? lastLoggedStatus = null;
        string? lastError = null;
        // Backdated so the first retry comes ~10s after the fallback (a Server that was merely still
        // starting, e.g. both services booting together), then once a minute.
        var lastConnectAttemptUtc = DateTime.UtcNow - TimeSpan.FromSeconds(50);

        while (!cancellationToken.IsCancellationRequested)
        {
            var pollDelay = TimeSpan.FromSeconds(5);
            try
            {
                EnrollStatusResponse? response;
                if (enrolled)
                {
                    using var statusRequest = new HttpRequestMessage(HttpMethod.Get, $"{baseUri}/rest/enroll/{_instanceId}/status");
                    statusRequest.Headers.Add(OrbitMeshHeaderNames.EnrollSecret, secret);
                    using var statusResponse = await client.SendAsync(statusRequest, cancellationToken);
                    statusResponse.EnsureSuccessStatusCode();
                    response = await statusResponse.Content.ReadFromJsonAsync<EnrollStatusResponse>(cancellationToken);
                    // The server forgot us (restart, expiry, dismissed by an admin) - enroll again
                    // rather than polling an entry that no longer exists forever.
                    if (response?.Status == EnrollmentStatus.Unknown)
                    {
                        enrolled = false;
                    }
                }
                else
                {
                    using var enrollRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUri}/rest/enroll") { Content = JsonContent.Create(request) };
                    enrollRequest.Headers.Add(OrbitMeshHeaderNames.EnrollSecret, secret);
                    using var postResponse = await client.SendAsync(enrollRequest, cancellationToken);
                    postResponse.EnsureSuccessStatusCode();
                    response = await postResponse.Content.ReadFromJsonAsync<EnrollStatusResponse>(cancellationToken);
                    // A rejected enrollment records nothing server-side, so there's no entry to poll.
                    enrolled = response?.Status != EnrollmentStatus.Rejected;
                }
                lastError = null;

                if (response?.Status == EnrollmentStatus.Rejected)
                {
                    // Not an enrollment at all: the Server already has an Edge with this InstanceId or name.
                    pollDelay = TimeSpan.FromSeconds(15);
                }

                if (response?.Status != lastLoggedStatus)
                {
                    if (response?.Status == EnrollmentStatus.Rejected)
                    {
                        logger.LogWarning("The server already has an Edge configured for this InstanceId/name, so this isn't a new enrollment - retrying the normal connection. If this persists, check Edge:OrbitMeshAccessKey (and give a different machine another Edge:EdgeName).");
                    }
                    else
                    {
                        logger.LogInformation("Enrollment status: {Status}", response?.Status ?? "?");
                    }
                    lastLoggedStatus = response?.Status;
                }

                var alreadyApplied = false;
                if (response is { Status: EnrollmentStatus.Approved, AccessKey: { Length: > 0 } accessKey })
                {
                    // The Server keeps an approved entry for a few minutes until the Edge connects for
                    // real. If what it hands out is already what this process started with, restarting
                    // again would loop forever - the connection is failing for some other reason.
                    alreadyApplied = accessKey == _options.OrbitMeshAccessKey
                        && (string.IsNullOrEmpty(response.EdgeName) || response.EdgeName == _edgeName);
                    if (!alreadyApplied)
                    {
                        await HandleApprovedAccessKeyAsync(accessKey, response.EdgeName);
                        return;
                    }
                }

                // "Rejected" = the Server already has an Edge configured for this InstanceId, and an
                // already-applied approval means the same: the original connect failure was likely
                // transient (e.g. the server was down at boot) rather than a missing approval - try the
                // normal connection again. Throttled to once a minute: if the AccessKey or name on disk
                // really is wrong every attempt is aborted, and the Server counts each one toward its
                // failed-attempt lockout (10 per 5 minutes per IP), which this must stay well under.
                if ((response?.Status == EnrollmentStatus.Rejected || alreadyApplied) && DateTime.UtcNow - lastConnectAttemptUtc > TimeSpan.FromSeconds(60))
                {
                    lastConnectAttemptUtc = DateTime.UtcNow;
                    if (await TryConnectAsync(cancellationToken))
                    {
                        logger.LogInformation("Connected to the OrbitMesh server - enrollment no longer needed.");
                        return;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Logged once per distinct failure (not every 5s) so a down server or a refused
                // enrollment is visible without flooding the log.
                if (ex.Message != lastError)
                {
                    logger.LogWarning(ex, "Enrollment request failed - will keep retrying");
                    lastError = ex.Message;
                }
            }

            try
            {
                await Task.Delay(pollDelay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
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

    // Fired once rest/enroll polling (EnrollAndPollAsync) sees an admin approve this Edge from the
    // Console's pending-edges list.
    private async Task HandleApprovedAccessKeyAsync(string accessKey, string? approvedEdgeName)
    {
        try
        {
            ApplyApprovedAccessKey(accessKey, approvedEdgeName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Received an approved AccessKey but couldn't save it to appsettings.json - apply it by hand instead.");
            return;
        }
        logger.LogInformation("Approved by the server - restarting to connect with the new AccessKey.");
        // _options only reflects appsettings.json at startup - restart to pick up the change just written.
        await RestartSelfAsync();
    }

    // approvedEdgeName: the admin may have approved this Edge under a different name than the one it
    // declared, and its credential is bound to that name - so it has to be adopted along with the key.
    private void ApplyApprovedAccessKey(string accessKey, string? approvedEdgeName)
    {
        var path = Path.Combine(_workingDirectory, "appsettings.json");
        var root = JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"{path} is empty or not valid JSON.");
        if (root["Edge"] is not JsonObject edgeSection)
        {
            throw new InvalidOperationException($"{path} has no \"Edge\" section to update.");
        }
        edgeSection["OrbitMeshAccessKey"] = accessKey;
        if (!string.IsNullOrEmpty(approvedEdgeName))
        {
            edgeSection["EdgeName"] = approvedEdgeName;
        }
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
                // The values travel as environment variables, not as trailing "-Name value" arguments:
                // with `powershell -Command <script>`, everything after the script text is appended to
                // the command itself instead of being bound to a param() block, so the previous
                // $ProcessId/$ServiceName were always null (the wait loop ended immediately) and the
                // service name was injectable into the command line.
                return TryStartServiceManagerWatcher("powershell.exe",
                    new Dictionary<string, string>
                    {
                        ["ORBITMESH_WATCH_PID"] = Environment.ProcessId.ToString(),
                        ["ORBITMESH_WATCH_NAME"] = RequireServiceOrUnitName()
                    },
                    "-NoProfile", "-NonInteractive", "-Command",
                    "$p = [int]$env:ORBITMESH_WATCH_PID; while (Get-Process -Id $p -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 500 }; sc.exe start $env:ORBITMESH_WATCH_NAME");
            }
            if (SystemdHelpers.IsSystemdService())
            {
                return TryStartServiceManagerWatcher("bash", null, "-c",
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

    // Must not start with '-' (would parse as an option to systemctl/sc.exe).
    private static readonly System.Text.RegularExpressions.Regex ServiceOrUnitNamePattern = new(@"^[A-Za-z0-9_.@][A-Za-z0-9_.@ -]*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private string RequireServiceOrUnitName()
    {
        var name = _options.UpdateOptions.ServiceOrUnitName ?? throw new InvalidOperationException("UpdateOptions.ServiceOrUnitName must be set to restart under a service manager.");
        return ServiceOrUnitNamePattern.IsMatch(name)
            ? name
            : throw new InvalidOperationException("UpdateOptions.ServiceOrUnitName contains characters that aren't valid in a service/unit name.");
    }

    // args are passed as real argv entries (not interpolated into the script string), so a
    // service/unit name never needs shell-escaping.
    private static bool TryStartServiceManagerWatcher(string executable, IReadOnlyDictionary<string, string>? environment, params string[] args)
    {
        var startInfo = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        if (environment != null)
        {
            foreach (var (key, value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }
        Process.Start(startInfo);
        return true;
    }

    // Returns whether the Server accepted the registration. A false is no longer retried here: the
    // Server aborts unauthorized connections outright, so a refusal means the credential isn't
    // accepted and REST enrollment (EnrollAndPollAsync) is the retry path.
    private async Task<bool> RegisterAsync()
    {
        try
        {
            var registered = await _connection!.InvokeAsync<bool>(EdgeServerMethodNames.RegisterEdge, _description);
            if (!registered)
            {
                logger.LogWarning("Edge registration was refused by the server");
                return false;
            }
            logger.LogInformation("Edge '{Name}' registered", _edgeName);
            lock (_lock)
            {
                foreach (var instance in _packages.Values)
                {
                    _ = _connection!.InvokeAsync(EdgeServerMethodNames.ReportPackageState, instance.Package, instance.State);
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to register the edge");
            return false;
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
