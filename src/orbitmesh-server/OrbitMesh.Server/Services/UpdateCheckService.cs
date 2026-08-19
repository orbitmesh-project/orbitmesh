using OrbitMesh.Server.Configuration;
using OrbitMesh.Updating;
using Microsoft.Extensions.Options;

namespace OrbitMesh.Server.Services;

/// <summary>Periodically checks the configured update server (see <see cref="UpdateOptions"/>) for a
/// newer OrbitMesh.Server release and caches the result in <see cref="UpdateStatus"/> for the
/// Management API / console to read. A no-op loop when UpdateOptions.ServerUrl is empty, so the
/// feature stays fully opt-in.</summary>
public sealed class UpdateCheckService(
    IOptionsMonitor<OrbitMeshOptions> options,
    ReleaseServerClient releaseServerClient,
    UpdateStatus status,
    ILogger<UpdateCheckService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        status.CurrentVersion = ServerVersion.Current;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!string.IsNullOrWhiteSpace(options.CurrentValue.UpdateOptions.ServerUrl))
            {
                await CheckNowAsync(stoppingToken);
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

    public async Task CheckNowAsync(CancellationToken cancellationToken = default)
    {
        var update = options.CurrentValue.UpdateOptions;
        if (string.IsNullOrWhiteSpace(update.ServerUrl))
        {
            status.Error = "No update server configured (OrbitMesh:UpdateOptions:ServerUrl is empty).";
            return;
        }
        try
        {
            var release = await releaseServerClient.GetLatestAsync(update, update.ProjectSlug, status.CurrentVersion, cancellationToken);
            status.LatestVersion = release?.Version;
            status.Changelog = release?.Changelog;
            status.ZipUrl = release?.ZipUrl;
            status.LastCheckedUtc = DateTimeOffset.UtcNow;
            status.Error = null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to check for OrbitMesh.Server updates");
            status.Error = ex.Message;
        }
    }
}
