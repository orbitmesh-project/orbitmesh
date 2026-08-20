using OrbitMesh.Server.Models;
using OrbitMesh.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace OrbitMesh.Server.Hubs;

/// <summary>Hub the Edge processes connect to: registration, package list push, control actions.</summary>
public sealed class EdgeHub(
    IOrbitMeshDirectory directory,
    IEdgeRegistry edgeRegistry,
    IPendingEdgeRegistry pendingEdgeRegistry,
    IPackageRegistry packageRegistry,
    OrbitMeshMetrics metrics,
    OrbitMeshLogService packageLog,
    IHubContext<ControlHub> controlHub,
    CredentialUsageTracker usageTracker,
    ILogger<EdgeHub> logger) : Hub
{
    private bool IsAuthorized() =>
        directory.CheckAccess(Context.GetEdgeName(), Context.GetPackageName(), Context.GetAccessKey(), OrbitMeshAccessType.OrbitMesh);

    public override async Task OnConnectedAsync()
    {
        Context.CaptureConnectionMetadata();
        if (IsAuthorized())
        {
            // Clear a stale pending entry if this InstanceId just connected successfully.
            if (Context.GetInstanceId() is { Length: > 0 } connectedInstanceId)
            {
                pendingEdgeRegistry.Remove(connectedInstanceId);
            }
            metrics.AccessGranted();
            metrics.EdgeConnected();
            usageTracker.RecordUse(directory.GetCredentialName(Context.GetAccessKey()));
            packageLog.Log(Context.GetEdgeName(), Context.GetPackageName(),
                $"Edge connected to server (connectionId='{Context.ConnectionId}')",
                LogLevel.Info);
            await base.OnConnectedAsync();
            return;
        }

        metrics.AccessDenied();
        var edgeName = Context.GetEdgeName();
        var instanceId = Context.GetInstanceId();
        if (string.IsNullOrEmpty(edgeName) || string.IsNullOrEmpty(instanceId))
        {
            // No InstanceId - not a modern Edge build, reject as before.
            logger.LogWarning(
                "EdgeHub connection rejected for EdgeName={EdgeName}. " +
                "Add a matching entry under OrbitMesh:Edges in appsettings.json with a valid Credential.",
                edgeName);
            Context.Abort();
            return;
        }

        // Unrecognized but well-formed - kept open (not aborted) so an approval can push the new
        // credential down this same connection. Every other hub method checks IsAuthorized() itself.
        RecordPendingAttempt(instanceId, edgeName);
        await base.OnConnectedAsync();
    }

    // An already-approved Edge never shows as pending again, even if a stale cached key from a
    // race causes one more unauthorized call - nothing else would clear it after that.
    private void RecordPendingAttempt(string instanceId, string edgeName)
    {
        if (directory.Current.Edges.Any(e => e.InstanceId == instanceId))
        {
            pendingEdgeRegistry.Remove(instanceId);
            return;
        }
        var remoteIp = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? "?";
        pendingEdgeRegistry.RecordAttempt(instanceId, edgeName, remoteIp, Context.ConnectionId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var edgeName = Context.GetEdgeName();
        edgeRegistry.Disconnect(Context.ConnectionId);
        metrics.SetEdgesConnected(edgeRegistry.Edges.Count(s => s.Value.IsConnected));
        if (edgeName != null)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, edgeName);
            metrics.EdgeDisconnected();
            packageLog.Log(edgeName, null,
                $"Edge disconnected from server (connectionId='{Context.ConnectionId}', exception='{exception?.Message ?? "none"}')",
                LogLevel.Info);
            await ControlHub.NotifyEdgeStateChangedAsync(controlHub, directory, edgeRegistry, edgeName);
        }
        await base.OnDisconnectedAsync(exception);
    }

    public Version GetServerVersion() => typeof(EdgeHub).Assembly.GetName().Version ?? new Version(1, 0, 0);

    public async Task<bool> RegisterEdge(EdgeDescription edgeDescription)
    {
        if (!IsAuthorized())
        {
            // Still pending - the Edge retries this every 3s, which doubles as a heartbeat.
            if (Context.GetInstanceId() is { Length: > 0 } instanceId && Context.GetEdgeName() is { Length: > 0 } pendingEdgeName)
            {
                RecordPendingAttempt(instanceId, pendingEdgeName);
            }
            return false;
        }

        var edgeName = Context.GetEdgeName()!;
        var info = new EdgeInfo { IsConnected = true, RegistrationDate = DateTime.Now, Description = edgeDescription };
        edgeRegistry.Register(Context.ConnectionId, edgeName, info);
        metrics.SetEdgesConnected(edgeRegistry.Edges.Count(s => s.Value.IsConnected));
        packageLog.Log(edgeName, null,
            $"Edge registered with the server (connectionId='{Context.ConnectionId}')",
            LogLevel.Info);
        await Groups.AddToGroupAsync(Context.ConnectionId, edgeName);
        await ControlHub.NotifyEdgeStateChangedAsync(controlHub, directory, edgeRegistry, edgeName);
        await PushPackageListAsync(edgeName);
        return true;
    }

    public void ReportPackageState(PackageDescription packageDescription, PackageState state)
    {
        if (!IsAuthorized())
        {
            return;
        }
        packageLog.Log(Context.GetEdgeName(), packageDescription.Name,
            $"Package reported state {state}",
            LogLevel.Info);
        _ = ControlHub.NotifyPackageStateReportedAsync(controlHub, packageRegistry, Context.GetEdgeName()!, packageDescription.Name, state, packageDescription);
    }

    public void ReportPackagesUsage(List<PackageUsageReport> packagesUsage)
    {
        if (!IsAuthorized())
        {
            return;
        }
        var edgeName = Context.GetEdgeName()!;
        foreach (var usage in packagesUsage)
        {
            _ = ControlHub.SendPackageUsageReportAsync(controlHub, edgeName, usage.PackageName, usage.CpuUsage, usage.RamUsage);
        }
    }

    public void WriteLog(string message, LogLevel level = LogLevel.Info)
    {
        if (!IsAuthorized())
        {
            return;
        }
        packageLog.Log(Context.GetEdgeName(), "Edge", message, level);
    }

    internal static async Task SendPackageControlActionAsync(IHubContext<EdgeHub> edgeHub, IHubContext<OrbitMeshHub> orbitmeshHub, IOrbitMeshDirectory directory, IEdgeRegistry edgeRegistry, string edgeName, string packageName, PackageControlAction action)
    {
        if (!edgeRegistry.IsConnected(edgeName))
        {
            return;
        }
        if (action != PackageControlAction.Start)
        {
            await orbitmeshHub.Clients.Group(directory.GetPackageInstanceId(edgeName, packageName)).SendAsync(EdgeClientMethodNames.PackageControlAction, action);
        }
        await edgeHub.Clients.Group(edgeName).SendAsync(EdgeClientMethodNames.PackageControlAction, new PackageControlActionRequest(packageName, action));
    }

    internal static async Task SendEdgeRestartAsync(IHubContext<EdgeHub> edgeHub, IEdgeRegistry edgeRegistry, string edgeName)
    {
        if (!edgeRegistry.IsConnected(edgeName))
        {
            return;
        }
        await edgeHub.Clients.Group(edgeName).SendAsync(EdgeServerMethodNames.RestartEdge);
    }

    internal static async Task SendEdgeCheckForUpdateAsync(IHubContext<EdgeHub> edgeHub, IEdgeRegistry edgeRegistry, string edgeName)
    {
        if (!edgeRegistry.IsConnected(edgeName))
        {
            return;
        }
        await edgeHub.Clients.Group(edgeName).SendAsync(EdgeServerMethodNames.CheckForUpdate);
    }

    private async Task PushPackageListAsync(string edgeName)
    {
        var packages = directory.GetPackagesList(edgeName, includeAccessKey: true);
        await Clients.Caller.SendAsync(EdgeClientMethodNames.PushPackagesList, packages);
    }
}

public sealed record PackageControlActionRequest(string PackageName, PackageControlAction Action);

public sealed record PackageUsageReport(string PackageName, double CpuUsage, long RamUsage);
