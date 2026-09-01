using System.Text.Json;
using Cronos;
using OrbitMesh.Server.Configuration;
using OrbitMesh.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace OrbitMesh.Server.Services;

/// <summary>
/// Fires a message on a cron schedule (see <see cref="ScheduledTaskOptions"/>) - the automation
/// equivalent of a human using the Console's Messages page. Runs every task as its configured
/// Credential, through the same <see cref="IOrbitMeshDirectory.CheckMessageAuthorization"/> and
/// <c>messages:execute</c> scope check any other sender goes through - a schedule is not a way
/// around the permission model, just another caller of it.
/// </summary>
public sealed class ScheduledTaskRunner(
    IOptionsMonitor<OrbitMeshOptions> options,
    IOrbitMeshConfigWriter configWriter,
    IOrbitMeshDirectory directory,
    AccessKeyCipher accessKeyCipher,
    IPackageRegistry packageRegistry,
    IHubContext<OrbitMeshHub> orbitmeshHub,
    PackageGroupManager packageGroupManager,
    IHubContext<ConsumerHub> consumerHub,
    ConsumerGroupManager consumerGroupManager,
    OrbitMeshMetrics metrics,
    ILogger<ScheduledTaskRunner> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    // A task with no LastRunUtc yet (brand new, or never successfully persisted) starts counting
    // occurrences from here rather than from the epoch - otherwise a task created at 19:00 for a
    // "0 20 * * *" schedule would see zero occurrences until 20:00 as expected, but one created a
    // year into a daily schedule would immediately see hundreds of "missed" occurrences.
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var task in options.CurrentValue.ScheduledTasks.Where(t => t.Enable))
            {
                try
                {
                    await ProcessTaskAsync(task);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Scheduled task '{Name}' failed unexpectedly", task.Name);
                }
            }
            try
            {
                await Task.Delay(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ProcessTaskAsync(ScheduledTaskOptions task)
    {
        CronExpression cron;
        try
        {
            cron = CronExpression.Parse(task.CronExpression);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Scheduled task '{Name}' has an invalid cron expression '{Cron}' - skipped.", task.Name, task.CronExpression);
            return;
        }

        var now = DateTime.UtcNow;
        var from = task.LastRunUtc ?? _startedAtUtc;
        // toInclusive so an occurrence landing exactly on a tick boundary isn't pushed to next tick.
        var occurrences = cron.GetOccurrences(from, now, TimeZoneInfo.Local, fromInclusive: false, toInclusive: true).ToList();
        if (occurrences.Count == 0)
        {
            return;
        }

        // More than one occurrence means the Server was down/unreachable through at least one of
        // them - CatchUpIfMissed decides whether that's a single "catch up now" fire or a silent skip.
        // Either way only ONE send happens here, not one per missed occurrence (see the option's doc
        // comment - this brings state back in line, it doesn't replay every missed day's action).
        var isCatchUp = occurrences.Count > 1;
        if (!isCatchUp || task.CatchUpIfMissed)
        {
            await FireAsync(task);
        }
        else
        {
            logger.LogInformation("Scheduled task '{Name}' skipped {Count} missed occurrence(s) (CatchUpIfMissed is off).", task.Name, occurrences.Count);
        }

        var lastOccurrence = occurrences[^1];
        task.LastRunUtc = lastOccurrence;
        configWriter.Update(cfg =>
        {
            var stored = cfg.ScheduledTasks.FirstOrDefault(t => t.Name.Equals(task.Name, StringComparison.OrdinalIgnoreCase));
            if (stored != null)
            {
                stored.LastRunUtc = lastOccurrence;
            }
        });
    }

    private async Task FireAsync(ScheduledTaskOptions task)
    {
        var credential = options.CurrentValue.Credentials.FirstOrDefault(c => c.Name.Equals(task.Credential, StringComparison.OrdinalIgnoreCase));
        if (credential is not { Enable: true })
        {
            logger.LogWarning("Scheduled task '{Name}': credential '{Credential}' doesn't exist or is disabled - message not sent.", task.Name, task.Credential);
            return;
        }
        if (!credential.Scopes.Contains(OrbitMeshScope.MessagesExecute))
        {
            logger.LogWarning("Scheduled task '{Name}': credential '{Credential}' is missing the 'messages:execute' scope - message not sent.", task.Name, task.Credential);
            return;
        }

        var accessKey = AccessKeyCipher.IsEncrypted(credential.AccessKey) ? accessKeyCipher.Decrypt(credential.AccessKey) : credential.AccessKey;
        var scope = MessageScope.Create(MessageScope.ScopeType.Package, $"{task.EdgeName}/{task.PackageName}");
        if (!directory.CheckMessageAuthorization(accessKey, scope, task.MessageKey))
        {
            logger.LogWarning("Scheduled task '{Name}': credential '{Credential}' is not authorized (Authorizations) to send '{Key}' - message not sent.", task.Name, task.Credential, task.MessageKey);
            return;
        }

        // Best-effort "is anyone even listening" check - Package/Edge-scoped sends go through named
        // SignalR groups directly (see OrbitMeshHub.SendMessageOnOrbitMesh), which have no API to
        // query membership, unlike this registry which already tracks live connection state for the
        // Console's own Fleet view.
        if (!packageRegistry.PackagesInfos.TryGetValue($"{task.EdgeName}/{task.PackageName}", out var info) || !info.IsConnected)
        {
            logger.LogWarning("Scheduled task '{Name}': package '{Edge}/{Package}' is not currently connected - message sent, but nothing is listening.", task.Name, task.EdgeName, task.PackageName);
        }

        object? data = null;
        if (!string.IsNullOrEmpty(task.Data))
        {
            try
            {
                data = JsonSerializer.Deserialize<JsonElement?>(task.Data);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Scheduled task '{Name}' has invalid JSON in Data - message not sent.", task.Name);
                return;
            }
        }

        var sender = new MessageSender { Type = MessageSender.SenderType.OrbitMeshHttp, ConnectionId = null, FriendlyName = $"ScheduledTask:{task.Name}" };
        await OrbitMeshHub.SendMessageOnOrbitMesh(orbitmeshHub.Clients, packageGroupManager, consumerHub, consumerGroupManager, metrics, sender, scope, task.MessageKey, data);
        logger.LogInformation("Scheduled task '{Name}' fired '{Key}' to '{Edge}/{Package}'.", task.Name, task.MessageKey, task.EdgeName, task.PackageName);
    }
}
