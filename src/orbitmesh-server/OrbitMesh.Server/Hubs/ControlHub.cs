using OrbitMesh.Server.Models;
using OrbitMesh.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace OrbitMesh.Server.Hubs;

/// <summary>Hub for the admin console: live edge/package state, logs, and remote control actions.</summary>
public sealed class ControlHub(
    IOrbitMeshDirectory directory,
    IEdgeRegistry edgeRegistry,
    IPackageRegistry packageRegistry,
    OrbitMeshTelemetryItemManager telemetryItems,
    IHubContext<EdgeHub> edgeHub,
    IHubContext<OrbitMeshHub> orbitmeshHub,
    OrbitMeshLogService packageLog,
    CredentialUsageTracker usageTracker,
    ILogger<ControlHub> logger) : Hub
{
    public const string LogsGroup = "ControlLogs";
    private const string EdgesGroup = "ControlEdges";
    private const string PackagesStateGroup = "ControlPackagesState";
    private const string PackagesUsageGroup = "ControlPackagesUsage";

    public override Task OnConnectedAsync()
    {
        Context.CaptureConnectionMetadata();
        if (!directory.CheckAccess(Context.GetEdgeName(), Context.GetPackageName(), Context.GetAccessKey(), OrbitMeshAccessType.Controller))
        {
            logger.LogWarning(
                "ControlHub connection rejected (EdgeName={EdgeName}). The credential must have at least one scope.",
                Context.GetEdgeName());
            Context.Abort();
            return Task.CompletedTask;
        }
        usageTracker.RecordUse(directory.GetCredentialName(Context.GetAccessKey()));
        return base.OnConnectedAsync();
    }

    // The connection-level gate above only checks that the credential holds SOME scope at all (see
    // IOrbitMeshDirectory.CanAccess) - it's what actually enables a safe read-only account (e.g. a public
    // demo with only *:read scopes): every mutating method here re-checks its own specific scope, so a
    // read-only credential can hold a live ControlHub connection and receive updates but every
    // control/write call below still rejects it.
    private bool HasScope(params string[] anyOf) => directory.FindCredential(Context.GetAccessKey())?.Scopes.Any(anyOf.Contains) == true;

    private void RequireScope(params string[] anyOf)
    {
        if (!HasScope(anyOf))
        {
            throw new HubException($"Forbidden: this action requires the '{string.Join("' or '", anyOf)}' scope.");
        }
    }

    // Group key strings match what the console's JS client sends (Scripts/signalr-client.js). Each
    // group is a live-push subscription for read-only data, so joining it needs the matching :read scope
    // - the same boundary RequestEdgesList/RequestPackagesList use for the equivalent on-demand pull.
    public Task AddToControlGroup(string group)
    {
        switch (group)
        {
            case "Edges": RequireScope(OrbitMeshScope.EdgesRead); return Groups.AddToGroupAsync(Context.ConnectionId, EdgesGroup);
            case "PackagesState": RequireScope(OrbitMeshScope.PackagesRead); return Groups.AddToGroupAsync(Context.ConnectionId, PackagesStateGroup);
            case "PackagesUsage": RequireScope(OrbitMeshScope.PackagesRead); return Groups.AddToGroupAsync(Context.ConnectionId, PackagesUsageGroup);
            case "PackagesLog": RequireScope(OrbitMeshScope.PackagesRead); return Groups.AddToGroupAsync(Context.ConnectionId, LogsGroup);
            default: return Task.CompletedTask;
        }
    }

    public Task RemoveToControlGroup(string group) => group switch
    {
        "Edges" => Groups.RemoveFromGroupAsync(Context.ConnectionId, EdgesGroup),
        "PackagesState" => Groups.RemoveFromGroupAsync(Context.ConnectionId, PackagesStateGroup),
        "PackagesUsage" => Groups.RemoveFromGroupAsync(Context.ConnectionId, PackagesUsageGroup),
        "PackagesLog" => Groups.RemoveFromGroupAsync(Context.ConnectionId, LogsGroup),
        _ => Task.CompletedTask
    };

    public async Task RequestEdgesList()
    {
        RequireScope(OrbitMeshScope.EdgesRead);
        var list = edgeRegistry.Edges.Values.Where(s => directory.EdgeExists(s.Description.EdgeName)).ToList();
        await Clients.Caller.SendAsync("UpdateEdgesList", new { List = list });
    }

    public async Task RequestEdgeUpdates()
    {
        RequireScope(OrbitMeshScope.EdgesRead);
        foreach (var (name, info) in edgeRegistry.Edges)
        {
            if (directory.EdgeExists(name))
            {
                await Clients.Caller.SendAsync("UpdateEdge", info);
            }
        }
    }

    public Task RestartEdge(string edgeName)
    {
        RequireScope(OrbitMeshScope.EdgesWrite);
        packageLog.Log(edgeName, null, "Edge restart requested by control hub", LogLevel.Info);
        return EdgeHub.SendEdgeRestartAsync(edgeHub, edgeRegistry, edgeName);
    }

    public Task CheckForEdgeUpdate(string edgeName)
    {
        RequireScope(OrbitMeshScope.UpdatesManage);
        packageLog.Log(edgeName, null, "Update check requested by control hub", LogLevel.Info);
        return EdgeHub.SendEdgeCheckForUpdateAsync(edgeHub, edgeRegistry, edgeName);
    }

    public async Task ReloadServerConfiguration(bool deployConfiguration)
    {
        RequireScope(OrbitMeshScope.ConfigurationWrite);
        if (!deployConfiguration)
        {
            return;
        }
        foreach (var edge in directory.Current.Edges)
        {
            var packages = directory.GetPackagesList(edge.Name, includeAccessKey: true);
            if (edgeRegistry.IsConnected(edge.Name))
            {
                await edgeHub.Clients.Group(edge.Name).SendAsync(EdgeClientMethodNames.PushPackagesList, packages);
            }
            foreach (var package in packages)
            {
                var packageInstanceId = directory.GetPackageInstanceId(edge.Name, package.Name);
                if (packageRegistry.TryGetPackageInfo(packageInstanceId, out var info) && info.IsConnected)
                {
                    var settings = directory.GetSettings(edge.Name, package.Name, substituteVariables: true);
                    await orbitmeshHub.Clients.Group(packageInstanceId).SendAsync(OrbitMeshClientMethodNames.UpdateSettings, settings);
                }
            }
        }
    }

    public async Task RequestPackagesList(string edgeName)
    {
        RequireScope(OrbitMeshScope.PackagesRead);
        await SendPackagesListAsync(Clients.Caller, edgeName);
    }

    public async Task RequestPackageDescriptor(string packageName)
    {
        RequireScope(OrbitMeshScope.PackagesRead);
        packageRegistry.PackageDescriptors.TryGetValue(packageName, out var descriptor);
        await Clients.Caller.SendAsync("UpdatePackageDescriptor", new { PackageName = packageName, Descriptor = descriptor });
    }

    public void PurgeTelemetryItems(string edgeName, string packageName, string name, string type)
    {
        RequireScope(OrbitMeshScope.TelemetryPurge);
        if (edgeName == "*" || packageName == "*")
        {
            return;
        }
        telemetryItems.PurgeTelemetryItems(edgeName, packageName, name, type);
    }

    public Task Start(string edgeName, string packageName)
    {
        RequireScope(OrbitMeshScope.PackagesControl);
        packageLog.Log(edgeName, packageName, "Package start requested", LogLevel.Info);
        return EdgeHub.SendPackageControlActionAsync(edgeHub, orbitmeshHub, directory, edgeRegistry, edgeName, packageName, PackageControlAction.Start);
    }

    public Task Restart(string edgeName, string packageName)
    {
        RequireScope(OrbitMeshScope.PackagesControl);
        packageLog.Log(edgeName, packageName, "Package restart requested", LogLevel.Info);
        return EdgeHub.SendPackageControlActionAsync(edgeHub, orbitmeshHub, directory, edgeRegistry, edgeName, packageName, PackageControlAction.Restart);
    }

    public Task Stop(string edgeName, string packageName)
    {
        RequireScope(OrbitMeshScope.PackagesControl);
        packageLog.Log(edgeName, packageName, "Package stop requested", LogLevel.Info);
        return EdgeHub.SendPackageControlActionAsync(edgeHub, orbitmeshHub, directory, edgeRegistry, edgeName, packageName, PackageControlAction.Stop);
    }

    public Task Reload(string edgeName, string packageName)
    {
        RequireScope(OrbitMeshScope.PackagesControl);
        packageLog.Log(edgeName, packageName, "Package reload requested", LogLevel.Info);
        return EdgeHub.SendPackageControlActionAsync(edgeHub, orbitmeshHub, directory, edgeRegistry, edgeName, packageName, PackageControlAction.Reload);
    }

    public async Task UpdatePackageSettings(string edgeName, string packageName)
    {
        RequireScope(OrbitMeshScope.PackagesManage);
        var settings = directory.GetSettings(edgeName, packageName, substituteVariables: true);
        await orbitmeshHub.Clients.Group(directory.GetPackageInstanceId(edgeName, packageName)).SendAsync(OrbitMeshClientMethodNames.UpdateSettings, settings);
    }

    internal static async Task NotifyEdgeStateChangedAsync(IHubContext<ControlHub> hub, IOrbitMeshDirectory directory, IEdgeRegistry edgeRegistry, string edgeName)
    {
        if (!edgeRegistry.Edges.TryGetValue(edgeName, out var info))
        {
            return;
        }
        await hub.Clients.Group(EdgesGroup).SendAsync("UpdateEdge", info);
    }

    internal static async Task NotifyPackageConnected(IHubContext<ControlHub> hub, IPackageRegistry packageRegistry, OrbitMeshMetrics metrics, string edgeName, string packageName, string connectionId, string? clientType, string? clientVersion, string? packageVersion)
    {
        if (edgeName == OrbitMeshDefaultNames.DeveloperEdgeName)
        {
            return;
        }
        var packageInstanceId = $"{edgeName}/{packageName}";
        if (!packageRegistry.TryGetPackageInfo(packageInstanceId, out var info))
        {
            return;
        }
        info.ConnectionId = connectionId;
        info.OrbitMeshClientVersion = clientVersion;
        info.OrbitMeshClientType = clientType;
        info.PackageVersion = packageVersion;
        info.LastUpdate = DateTime.Now;
        info.State = PackageState.Started;
        info.IsConnected = true;
        metrics.SetPackagesConnected(packageRegistry.PackagesInfos.Count(p => p.Value.IsConnected));
        await SendPackageStateReportAsync(hub, edgeName, info);
    }

    internal static async Task NotifyPackageDisconnected(IHubContext<ControlHub> hub, IPackageRegistry packageRegistry, OrbitMeshMetrics metrics, string edgeName, string packageName, string connectionId)
    {
        if (edgeName == OrbitMeshDefaultNames.DeveloperEdgeName)
        {
            return;
        }
        var packageInstanceId = $"{edgeName}/{packageName}";
        if (!packageRegistry.TryGetPackageInfo(packageInstanceId, out var info) || info.ConnectionId != connectionId)
        {
            return;
        }
        info.LastUpdate = DateTime.Now;
        info.IsConnected = false;
        metrics.SetPackagesConnected(packageRegistry.PackagesInfos.Count(p => p.Value.IsConnected));
        await SendPackageStateReportAsync(hub, edgeName, info);
    }

    internal static async Task NotifyPackageStateChanged(IHubContext<ControlHub> hub, IPackageRegistry packageRegistry, string edgeName, string packageName, PackageState state) =>
        await NotifyPackageStateReportedAsync(hub, packageRegistry, edgeName, packageName, state);

    internal static async Task NotifyPackageStateReportedAsync(IHubContext<ControlHub> hub, IPackageRegistry packageRegistry, string edgeName, string packageName, PackageState state, PackageDescription? packageDescription = null)
    {
        if (edgeName == OrbitMeshDefaultNames.DeveloperEdgeName)
        {
            return;
        }
        var packageInstanceId = $"{edgeName}/{packageName}";
        if (!packageRegistry.TryGetPackageInfo(packageInstanceId, out var info))
        {
            return;
        }
        if (packageDescription != null)
        {
            info.Package = packageDescription;
        }
        var isTransientNoop = (state == PackageState.Stopping && info.State == PackageState.Stopped)
            || (state == PackageState.Starting && info.State == PackageState.Started);
        if (info.State != state && !isTransientNoop)
        {
            info.State = state;
            info.LastUpdate = DateTime.Now;
        }
        await SendPackageStateReportAsync(hub, edgeName, info);
    }

    internal static async Task SendPackageUsageReportAsync(IHubContext<ControlHub> hub, string edgeName, string packageName, double cpuUsage, long ramUsage)
    {
        await hub.Clients.Group(PackagesUsageGroup).SendAsync("ReportPackageUsage", new
        {
            EdgeName = edgeName,
            PackageName = packageName,
            LastUpdate = DateTime.Now,
            CPU = cpuUsage,
            RAM = ramUsage
        });
    }

    private static async Task SendPackageStateReportAsync(IHubContext<ControlHub> hub, string edgeName, PackageInfo info)
    {
        await hub.Clients.Group(PackagesStateGroup).SendAsync("ReportPackageState", new
        {
            EdgeName = edgeName,
            PackageName = info.Package.Name,
            info.IsConnected,
            info.ConnectionId,
            info.LastUpdate,
            State = info.State.ToString(),
            info.PackageVersion,
            info.OrbitMeshClientVersion,
            info.OrbitMeshClientType,
            info.Package
        });
    }

    private async Task SendPackagesListAsync(ISingleClientProxy recipient, string edgeName)
    {
        var list = packageRegistry.PackagesInfos
            .Where(p => p.Key.StartsWith(edgeName + "/", StringComparison.OrdinalIgnoreCase))
            .Select(p => new
            {
                EdgeName = edgeName,
                p.Value.Package,
                p.Value.ConnectionId,
                p.Value.IsConnected,
                p.Value.LastUpdate,
                State = p.Value.State.ToString(),
                p.Value.PackageVersion,
                p.Value.OrbitMeshClientVersion,
                p.Value.OrbitMeshClientType
            }).ToList();
        await recipient.SendAsync("UpdatePackageList", new { EdgeName = edgeName, List = list });
    }
}
