using OrbitMesh.Edge.Configuration;
using OrbitMesh.Updating;
using Microsoft.Extensions.Options;

namespace OrbitMesh.Edge.Services;

/// <summary>Periodically checks the configured update server for a newer OrbitMesh.Edge release and
/// applies it automatically as soon as one is found. Also reachable on demand - see
/// <see cref="CheckAndApplyAsync"/> - when the Server pushes EdgeServerMethodNames.CheckForUpdate
/// (Console's "check for update" button, EdgeManager wires it up). A no-op loop when ServerUrl is
/// empty, same opt-in convention as the Server's own UpdateCheckService - the on-demand path skips
/// that gate, since an explicit admin request wouldn't have anywhere to go otherwise.</summary>
public sealed class EdgeUpdateCheckService(
    IOptionsMonitor<EdgeOptions> options,
    ReleaseServerClient releaseServerClient,
    EdgeSelfUpdater edgeSelfUpdater,
    ILogger<EdgeUpdateCheckService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!string.IsNullOrWhiteSpace(options.CurrentValue.UpdateOptions.ServerUrl))
            {
                await CheckAndApplyAsync(stoppingToken);
            }
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(options.CurrentValue.UpdateOptions.CheckIntervalMinutes, 5)), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task CheckAndApplyAsync(CancellationToken cancellationToken = default)
    {
        var update = options.CurrentValue.UpdateOptions;
        try
        {
            var release = await releaseServerClient.GetLatestAsync(update, update.ProjectSlug, EdgeVersion.Current, cancellationToken);
            if (release?.ZipUrl == null || !UpdateVersionComparer.IsNewer(release.Version, EdgeVersion.Current))
            {
                return;
            }
            logger.LogInformation("Applying OrbitMesh.Edge update {Version} (currently {Current})...", release.Version, EdgeVersion.Current);
            await edgeSelfUpdater.ApplyAsync(release.ZipUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to check for or apply OrbitMesh.Edge updates");
        }
    }
}
