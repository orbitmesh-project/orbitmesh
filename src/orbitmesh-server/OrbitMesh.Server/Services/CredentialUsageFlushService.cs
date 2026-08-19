using Microsoft.Extensions.Options;
using OrbitMesh.Server.Configuration;

namespace OrbitMesh.Server.Services;

/// <summary>Periodically persists CredentialUsageTracker's in-memory timestamps into appsettings.json
/// (LastUsedUtc) - see CredentialUsageTracker for why this is batched instead of written per-request.</summary>
public sealed class CredentialUsageFlushService(
    CredentialUsageTracker tracker,
    IOrbitMeshConfigWriter configWriter,
    ILogger<CredentialUsageFlushService> logger) : BackgroundService
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(FlushInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var dirtyNames = tracker.TakeDirtyNames();
            if (dirtyNames.Count == 0)
            {
                continue;
            }

            try
            {
                configWriter.Update(cfg =>
                {
                    foreach (var name in dirtyNames)
                    {
                        var credential = cfg.Credentials.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                        var when = tracker.GetLastUsed(name);
                        if (credential != null && when != null)
                        {
                            credential.LastUsedUtc = when;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to flush credential usage timestamps for {Names}", string.Join(", ", dirtyNames));
            }
        }
    }
}
