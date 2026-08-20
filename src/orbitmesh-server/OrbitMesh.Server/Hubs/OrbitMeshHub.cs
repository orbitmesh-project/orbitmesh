using OrbitMesh.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace OrbitMesh.Server.Hubs;

/// <summary>The main hub packages connect to: messages, telemetry items, settings, package descriptors.</summary>
public sealed class OrbitMeshHub(
    IOrbitMeshDirectory directory,
    PackageGroupManager groupManager,
    ConsumerGroupManager consumerGroupManager,
    IPackageRegistry packageRegistry,
    IEdgeRegistry edgeRegistry,
    OrbitMeshTelemetryItemManager telemetryItems,
    OrbitMeshMetrics metrics,
    OrbitMeshLogService packageLog,
    IHubContext<ConsumerHub> consumerHub,
    IHubContext<ControlHub> controlHub,
    CredentialUsageTracker usageTracker,
    ILogger<OrbitMeshHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        Context.CaptureConnectionMetadata();
        if (!directory.CheckAccess(Context.GetEdgeName(), Context.GetPackageName(), Context.GetAccessKey(), OrbitMeshAccessType.OrbitMesh))
        {
            metrics.AccessDenied();
            logger.LogWarning(
                "OrbitMeshHub connection rejected (EdgeName={EdgeName}, PackageName={PackageName}). " +
                "Check that a matching Edge/Credential is configured in appsettings.json.",
                Context.GetEdgeName(), Context.GetPackageName());
            Context.Abort();
            return;
        }
        metrics.AccessGranted();
        metrics.PackageConnected();
        usageTracker.RecordUse(directory.GetCredentialName(Context.GetAccessKey()));

        var edgeName = Context.GetEdgeName()!;
        var packageName = Context.GetPackageName()!;
        var packageInstanceId = Context.GetPackageInstanceId();

        await Groups.AddToGroupAsync(Context.ConnectionId, packageInstanceId);
        await Groups.AddToGroupAsync(Context.ConnectionId, edgeName);
        await Groups.AddToGroupAsync(Context.ConnectionId, packageName);

        foreach (var group in directory.GetGroups(edgeName, packageName))
        {
            groupManager.Add(Context.ConnectionId, $"Message/{group}");
        }

        if (!Context.IsReconnection())
        {
            telemetryItems.PurgeTelemetryItems(edgeName, packageName);
        }

        await ControlHub.NotifyPackageConnected(controlHub, packageRegistry, metrics, edgeName, packageName, Context.ConnectionId,
            Context.GetOrbitMeshClientType(), Context.GetOrbitMeshClientVersion(), Context.GetPackageVersion());

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var edgeName = Context.GetEdgeName();
        var packageName = Context.GetPackageName();
        if (edgeName != null && packageName != null && !edgeRegistry.IsConnected(edgeName))
        {
            await ControlHub.NotifyPackageStateChanged(controlHub, packageRegistry, edgeName, packageName, PackageState.Stopped);
        }
        metrics.PackageDisconnected();
        groupManager.Remove(Context.ConnectionId);
        if (edgeName != null && packageName != null)
        {
            await ControlHub.NotifyPackageDisconnected(controlHub, packageRegistry, metrics, edgeName, packageName, Context.ConnectionId);
        }
        await base.OnDisconnectedAsync(exception);
    }

    public void SubscribeMessages(string group)
    {
        if (directory.CheckMessageGroupAuthorization(Context.GetAccessKey(), group))
        {
            groupManager.Add(Context.ConnectionId, $"Message/{group}");
        }
    }

    public void UnSubscribeMessages(string group) => groupManager.Remove(Context.ConnectionId, $"Message/{group}");

    public void DeclarePackageDescriptor(PackageDescriptor packageDescriptor) => packageRegistry.SetDescriptor(Context.GetPackageName()!, packageDescriptor);

    public async Task SendMessage(MessageScope scope, string key, object? data)
    {
        if (!directory.CheckMessageAuthorization(Context.GetAccessKey(), scope, key))
        {
            return;
        }
        var sender = new MessageSender { Type = MessageSender.SenderType.OrbitMeshHub, ConnectionId = Context.ConnectionId, FriendlyName = Context.GetPackageInstanceId() };
        await SendMessageOnOrbitMesh(Clients, groupManager, consumerHub, consumerGroupManager, metrics, sender, scope, key, data);
    }

    public void RequestSettings()
    {
        var settings = directory.GetSettings(Context.GetEdgeName()!, Context.GetPackageName()!, substituteVariables: true);
        _ = Clients.Caller.SendAsync(OrbitMeshClientMethodNames.UpdateSettings, settings);
    }

    public void PushTelemetryItem(string name, object? value, string type = "", Dictionary<string, object>? metadatas = null, int lifetime = 0)
    {
        var telemetryItem = telemetryItems.PushTelemetryItem(Context.GetEdgeName()!, Context.GetPackageName()!, name, value, type, metadatas, lifetime);
        _ = PushToSubscribersAsync(telemetryItem);
    }

    public void PurgeTelemetryItems(string name, string type) => telemetryItems.PurgeTelemetryItems(Context.GetEdgeName()!, Context.GetPackageName()!, name, type);

    public void WriteLog(string message, LogLevel level = LogLevel.Info) => packageLog.Log(Context.GetEdgeName(), Context.GetPackageName(), message, level);

    public async Task RequestTelemetryItems(string edgeName, string packageName, string name, string type)
    {
        try
        {
            var count = 0;
            foreach (var so in telemetryItems.GetTelemetryItemsWithAccessKey(directory, Context.GetAccessKey(), edgeName, packageName, name, type))
            {
                count++;
                await Clients.Caller.SendAsync(OrbitMeshClientMethodNames.UpdateTelemetryItem, so);
            }
            metrics.UpdateTelemetryItemSent(count);
        }
        catch (UnauthorizedAccessException)
        {
            // Silently ignore, matching original behaviour.
        }
    }

    public void SubscribeTelemetryItems(string edgeName, string packageName, string name, string type)
    {
        if (directory.CheckTelemetryItemAuthorization(Context.GetAccessKey(), edgeName, packageName, name))
        {
            groupManager.Add(Context.ConnectionId, $"SO/{edgeName}/{packageName}/{name}/{type}");
        }
    }

    public void UnSubscribeTelemetryItems(string edgeName, string packageName, string name, string type) =>
        groupManager.Remove(Context.ConnectionId, $"SO/{edgeName}/{packageName}/{name}/{type}");

    private async Task PushToSubscribersAsync(TelemetryItem telemetryItem)
    {
        var groups = CreateTelemetryItemGroupList(telemetryItem);
        await UpdateTelemetryItemToClientsAsync(Clients, groupManager, metrics, groups, telemetryItem);
        await UpdateTelemetryItemToClientsAsync(consumerHub.Clients, consumerGroupManager, metrics, groups, telemetryItem);
    }

    internal static async Task SendMessageOnOrbitMesh(IHubClients<IClientProxy> callerClients, PackageGroupManager groupManager, IHubContext<ConsumerHub> consumerHub, ConsumerGroupManager consumerGroupManager, OrbitMeshMetrics metrics, MessageSender sender, MessageScope scope, string key, object? data)
    {
        if (scope.Scope == MessageScope.ScopeType.None)
        {
            return;
        }
        metrics.SendMessage();
        var message = new MessageEventArgs { Key = key, Data = data, Sender = sender, Scope = scope };

        switch (scope.Scope)
        {
            case MessageScope.ScopeType.All:
                await callerClients.All.SendAsync(OrbitMeshClientMethodNames.ReceiveMessage, message);
                break;
            case MessageScope.ScopeType.Others:
                if (sender.Type == MessageSender.SenderType.OrbitMeshHub)
                {
                    await callerClients.AllExcept(sender.ConnectionId!).SendAsync(OrbitMeshClientMethodNames.ReceiveMessage, message);
                }
                else
                {
                    await callerClients.All.SendAsync(OrbitMeshClientMethodNames.ReceiveMessage, message);
                }
                break;
            case MessageScope.ScopeType.Group:
                if (scope.Args.Count > 0)
                {
                    var groups = scope.Args.Select(g => $"Message/{g}").Append("Message/*");
                    var connectionIds = groupManager.GetDistinctConnectionIdsOnGroupList(groups);
                    if (connectionIds.Count > 0)
                    {
                        await callerClients.Clients(connectionIds).SendAsync(OrbitMeshClientMethodNames.ReceiveMessage, message);
                    }
                }
                break;
            case MessageScope.ScopeType.Package:
            case MessageScope.ScopeType.Edge:
                if (scope.Args.Count > 0)
                {
                    // Args can be either a package/edge name (join a named group in OnConnectedAsync)
                    // or a raw SignalR connection ID (saga responses target the caller's own connection
                    // directly via CreateResponseScope). ASP.NET Core SignalR connection IDs aren't
                    // GUID-formatted like the original SignalR 2 ones, so there's no reliable way to tell
                    // them apart by shape - sending to both is harmless: SignalR no-ops for a connection ID
                    // that isn't currently connected and for a group with no members.
                    await callerClients.Clients(scope.Args).SendAsync(OrbitMeshClientMethodNames.ReceiveMessage, message);
                    await callerClients.Groups(scope.Args).SendAsync(OrbitMeshClientMethodNames.ReceiveMessage, message);
                }
                break;
        }

        metrics.MessageDelivered();
        await ConsumerHub.SendMessageToConsumersAsync(consumerHub, consumerGroupManager, metrics, message);
    }

    private static async Task UpdateTelemetryItemToClientsAsync(IHubClients<IClientProxy> clients, OrbitMeshGroupManager groupManager, OrbitMeshMetrics metrics, List<string> groups, TelemetryItem telemetryItem)
    {
        var connectionIds = groupManager.GetDistinctConnectionIdsOnGroupList(groups);
        if (connectionIds.Count > 0)
        {
            await clients.Clients(connectionIds).SendAsync(OrbitMeshClientMethodNames.UpdateTelemetryItem, telemetryItem);
            metrics.UpdateTelemetryItemSent(connectionIds.Count);
        }
    }

    private static List<string> CreateTelemetryItemGroupList(TelemetryItem telemetryItem)
    {
        // A HashSet<string> dedupes as we build (needed since a real value can coincide with the "*"
        // wildcard) without the extra List + Distinct() + ToList() pass the old version did.
        var groups = new HashSet<string>(16);
        foreach (var edge in new[] { "*", telemetryItem.EdgeName })
        foreach (var package in new[] { "*", telemetryItem.PackageName })
        foreach (var name in new[] { "*", telemetryItem.Name })
        foreach (var type in new[] { "*", telemetryItem.Type ?? string.Empty })
        {
            groups.Add($"SO/{edge}/{package}/{name}/{type}");
        }
        return groups.ToList();
    }
}
