using OrbitMesh.Server.Hubs;
using OrbitMesh.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace OrbitMesh.Server.Controllers;

/// <summary>
/// HTTP mirror of OrbitMeshHub's fire-and-forget actions, for simple clients that can't hold a
/// persistent SignalR connection (e.g. shell scripts, the console's "request all telemetry items" refresh).
/// Routes match the original server's PascalCase action names since the console's JS still calls them.
/// Long-poll subscriptions (GetMessages/GetTelemetryItems) from the original are not carried over in this
/// pass - use the SignalR hub for live delivery.
/// </summary>
[ApiController]
[Route("rest/orbitmesh")]
public sealed class OrbitMeshController(
    IOrbitMeshDirectory directory,
    OrbitMeshTelemetryItemManager telemetryItems,
    PackageGroupManager packageGroupManager,
    ConsumerGroupManager consumerGroupManager,
    IHubContext<OrbitMeshHub> orbitmeshHub,
    IHubContext<ConsumerHub> consumerHub,
    OrbitMeshMetrics metrics) : ControllerBase
{
    [HttpGet("CheckAccess")]
    public IActionResult CheckAccess() => Ok();

    [HttpPost("SendMessage")]
    public Task<IActionResult> SendMessage([FromBody] SendMessageRequest request) =>
        SendMessageCore(request.Scope, request.Key, request.Data);

    [HttpGet("SendMessage")]
    public Task<IActionResult> SendMessage(MessageScope.ScopeType scope, string key, string? data = null, string? args = null, string? sagaId = null)
    {
        var messageScope = MessageScope.Create(scope, string.IsNullOrEmpty(args) ? null : args.Split(','));
        if (!string.IsNullOrEmpty(sagaId))
        {
            messageScope.SagaId = sagaId;
        }
        return SendMessageCore(messageScope, key, data);
    }

    private async Task<IActionResult> SendMessageCore(MessageScope scope, string key, object? data)
    {
        if (!directory.CheckMessageAuthorization(GetAccessKey(), scope, key))
        {
            return Forbid();
        }
        var sender = new MessageSender { Type = MessageSender.SenderType.OrbitMeshHttp, ConnectionId = null, FriendlyName = $"{GetHeader(OrbitMeshHeaderNames.EdgeName)}/{GetHeader(OrbitMeshHeaderNames.PackageName)}" };
        await OrbitMeshHub.SendMessageOnOrbitMesh(orbitmeshHub.Clients, packageGroupManager, consumerHub, consumerGroupManager, metrics, sender, scope, key, data);
        return Ok();
    }

    [HttpPost("PushTelemetryItem")]
    public IActionResult PushTelemetryItem([FromBody] TelemetryItem telemetryItem)
    {
        telemetryItems.PushTelemetryItem(GetHeader(OrbitMeshHeaderNames.EdgeName)!, GetHeader(OrbitMeshHeaderNames.PackageName)!, telemetryItem.Name, telemetryItem.Value, telemetryItem.Type ?? string.Empty, telemetryItem.Metadatas, telemetryItem.Lifetime);
        return Ok();
    }

    [HttpGet("PurgeTelemetryItems")]
    public IActionResult PurgeTelemetryItems(string name = "*", string type = "*")
    {
        telemetryItems.PurgeTelemetryItems(GetHeader(OrbitMeshHeaderNames.EdgeName)!, GetHeader(OrbitMeshHeaderNames.PackageName)!, name, type);
        return Ok();
    }

    [HttpGet("GetSettings")]
    public ActionResult<Dictionary<string, string>> GetSettings() =>
        directory.GetSettings(GetHeader(OrbitMeshHeaderNames.EdgeName)!, GetHeader(OrbitMeshHeaderNames.PackageName)!, substituteVariables: true);

    [HttpGet("RequestTelemetryItems")]
    public ActionResult<IEnumerable<TelemetryItem>> RequestTelemetryItems(string edge = "*", string package = "*", string name = "*", string type = "*")
    {
        try
        {
            return telemetryItems.GetTelemetryItemsWithAccessKey(directory, GetAccessKey(), edge, package, name, type).ToList();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    private string? GetAccessKey() => GetHeader(OrbitMeshHeaderNames.AccessKey);

    private string? GetHeader(string key) => Request.GetHeaderOrQuery(key);
}

public sealed class SendMessageRequest
{
    public required MessageScope Scope { get; set; }

    public required string Key { get; set; }

    public object? Data { get; set; }
}
