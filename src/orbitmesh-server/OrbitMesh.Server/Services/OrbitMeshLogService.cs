using OrbitMesh.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace OrbitMesh.Server.Services;

/// <summary>
/// Single funnel for logs emitted by edges and packages: writes to the standard logging pipeline and
/// live-broadcasts to the admin console via ControlHub (replaces the original's custom NLog target).
/// </summary>
public sealed class OrbitMeshLogService(IHubContext<ControlHub> controlHub, ILogger<OrbitMeshLogService> logger, OrbitMeshMetrics metrics)
{
    public void Log(string? edgeName, string? packageName, string message, LogLevel level)
    {
        var msLevel = level switch
        {
            LogLevel.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
            LogLevel.Info => Microsoft.Extensions.Logging.LogLevel.Information,
            LogLevel.Warn => Microsoft.Extensions.Logging.LogLevel.Warning,
            LogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
            LogLevel.Fatal => Microsoft.Extensions.Logging.LogLevel.Critical,
            _ => Microsoft.Extensions.Logging.LogLevel.Information
        };
        logger.Log(msLevel, "[{Edge}/{Package}] {Message}", edgeName, packageName, message);
        metrics.WriteLog();

        _ = controlHub.Clients.Group(ControlHub.LogsGroup).SendAsync("ReceiveLog", new
        {
            EdgeName = edgeName,
            PackageName = packageName,
            Message = message,
            Level = level.ToString(),
            // The console's JS reads "message.Date" (new Date(message.Date)) - not "Timestamp".
            Date = DateTime.Now
        });
    }
}
