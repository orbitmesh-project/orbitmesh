using OrbitMesh.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace OrbitMesh.Server.Hubs;

/// <summary>Hub for read-mostly external consumers: subscribe to messages/telemetry items, no package registration.</summary>
public sealed class ConsumerHub(
    IOrbitMeshDirectory directory,
    ConsumerGroupManager groupManager,
    PackageGroupManager packageGroupManager,
    OrbitMeshTelemetryItemManager telemetryItems,
    OrbitMeshMetrics metrics,
    IHubContext<OrbitMeshHub> orbitmeshHub,
    IHubContext<ConsumerHub> selfHub,
    ILogger<ConsumerHub> logger) : Hub
{
    public override Task OnConnectedAsync()
    {
        Context.CaptureConnectionMetadata();
        if (!directory.CheckAccess(OrbitMeshDefaultNames.ConsumerEdgeName, OrbitMeshDefaultNames.ConsumerEdgeName, Context.GetAccessKey(), OrbitMeshAccessType.OrbitMesh))
        {
            metrics.AccessDenied();
            logger.LogWarning("ConsumerHub connection rejected: no enabled credential matches the supplied AccessKey.");
            Context.Abort();
            return Task.CompletedTask;
        }
        metrics.AccessGranted();
        metrics.ConsumerConnected();
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        metrics.ConsumerDisconnected();
        groupManager.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

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
        }
    }

    public void SubscribeTelemetryItems(string edgeName, string packageName, string name, string type)
    {
        if (directory.CheckTelemetryItemAuthorization(Context.GetAccessKey(), edgeName, packageName, name))
        {
            groupManager.Add(Context.ConnectionId, $"SO/{edgeName}/{packageName}/{name}/{type}");
            metrics.SubscribeTelemetryItems();
        }
    }

    public void UnSubscribeTelemetryItems(string edgeName, string packageName, string name, string type) =>
        groupManager.Remove(Context.ConnectionId, $"SO/{edgeName}/{packageName}/{name}/{type}");

    public void SubscribeMessages(string group)
    {
        if (directory.CheckMessageGroupAuthorization(Context.GetAccessKey(), group))
        {
            groupManager.Add(Context.ConnectionId, $"Message/{group}");
        }
    }

    public void UnSubscribeMessages(string group) => groupManager.Remove(Context.ConnectionId, $"Message/{group}");

    public async Task SendMessage(MessageScope scope, string key, object? data)
    {
        if (!directory.CheckMessageAuthorization(Context.GetAccessKey(), scope, key))
        {
            return;
        }
        var sender = new MessageSender { Type = MessageSender.SenderType.ConsumerHub, ConnectionId = Context.ConnectionId, FriendlyName = Context.GetPackageInstanceId() };
        // Regardless of origin, message fan-out is always driven through OrbitMeshHub's package-side
        // group space first, then relayed to consumers - matching the original single funnel method.
        await OrbitMeshHub.SendMessageOnOrbitMesh(orbitmeshHub.Clients, packageGroupManager, selfHub, groupManager, metrics, sender, scope, key, data);
    }

    internal static async Task SendMessageToConsumersAsync(IHubContext<ConsumerHub> hub, ConsumerGroupManager groupManager, OrbitMeshMetrics metrics, MessageEventArgs message)
    {
        var clients = hub.Clients;
        switch (message.Scope.Scope)
        {
            case MessageScope.ScopeType.All:
                await clients.All.SendAsync(OrbitMeshClientMethodNames.ReceiveMessage, message);
                break;
            case MessageScope.ScopeType.Others:
                if (message.Sender.Type == MessageSender.SenderType.ConsumerHub)
                {
                    await clients.AllExcept(message.Sender.ConnectionId!).SendAsync(OrbitMeshClientMethodNames.ReceiveMessage, message);
                }
                else
                {
                    await clients.All.SendAsync(OrbitMeshClientMethodNames.ReceiveMessage, message);
                }
                break;
            case MessageScope.ScopeType.Group:
                if (message.Scope.Args.Count > 0)
                {
                    var groups = message.Scope.Args.Select(g => $"Message/{g}").Append("Message/*");
                    var connectionIds = groupManager.GetDistinctConnectionIdsOnGroupList(groups);
                    if (connectionIds.Count > 0)
                    {
                        await clients.Clients(connectionIds).SendAsync(OrbitMeshClientMethodNames.ReceiveMessage, message);
                    }
                }
                break;
            case MessageScope.ScopeType.Package:
            case MessageScope.ScopeType.Edge:
                // Consumers never join package/edge-name groups (only OrbitMeshHub connections
                // do), so the only thing that can match here is a saga response targeting this consumer's
                // own connection ID directly (see the matching comment in OrbitMeshHub).
                if (message.Scope.Args.Count > 0)
                {
                    await clients.Clients(message.Scope.Args).SendAsync(OrbitMeshClientMethodNames.ReceiveMessage, message);
                }
                break;
        }
    }
}
