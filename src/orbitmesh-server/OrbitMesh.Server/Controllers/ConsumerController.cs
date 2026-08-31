using OrbitMesh.Server.Hubs;
using OrbitMesh.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace OrbitMesh.Server.Controllers;

[ApiController]
[Route("rest/consumer")]
public sealed class ConsumerController(
    IOrbitMeshDirectory directory,
    OrbitMeshTelemetryItemManager telemetryItems,
    PackageGroupManager packageGroupManager,
    ConsumerGroupManager consumerGroupManager,
    IHubContext<OrbitMeshHub> orbitmeshHub,
    IHubContext<ConsumerHub> consumerHub,
    OrbitMeshMetrics metrics,
    SagaRegistry sagaRegistry) : ControllerBase
{
    [HttpGet("CheckAccess")]
    public IActionResult CheckAccess() => Ok();

    [HttpPost("SendMessage")]
    [RequiresScope(OrbitMeshScope.MessagesExecute)]
    public Task<IActionResult> SendMessage([FromBody] SendMessageRequest request) => SendMessageCore(request.Scope, request.Key, request.Data);

    [HttpGet("SendMessage")]
    [RequiresScope(OrbitMeshScope.MessagesExecute)]
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
        var sender = new MessageSender { Type = MessageSender.SenderType.ConsumerHttp, ConnectionId = null, FriendlyName = "Consumer" };
        if (scope.IsSaga && key != OrbitMeshDefaultNames.SagaResponseMessageKey)
        {
            sagaRegistry.RegisterRequest(scope.SagaId!, sender);
        }
        await OrbitMeshHub.SendMessageOnOrbitMesh(orbitmeshHub.Clients, packageGroupManager, consumerHub, consumerGroupManager, metrics, sender, scope, key, data);
        return Ok();
    }

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

    private string? GetAccessKey() => Request.GetHeaderOrQuery(OrbitMeshHeaderNames.AccessKey);
}
