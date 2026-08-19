using OrbitMesh.Server.Configuration;
using OrbitMesh.Updating;
using Microsoft.Extensions.Options;

namespace OrbitMesh.Server.Services;

/// <summary>Periodically checks every configured static site (Console, or any other) against the
/// update server, same schedule and same opt-in-via-empty-ServerUrl behavior as
/// <see cref="UpdateCheckService"/> - the two run side by side rather than one covering the other,
/// since a static site's "current version" comes from a marker file on disk
/// (see <see cref="StaticSiteUpdater"/>) instead of an assembly version.</summary>
public sealed class StaticSiteUpdateCheckService(
    IOptionsMonitor<OrbitMeshOptions> options,
    ReleaseServerClient releaseServerClient,
    StaticSiteUpdateRegistry registry,
    ILogger<StaticSiteUpdateCheckService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!string.IsNullOrWhiteSpace(options.CurrentValue.UpdateOptions.ServerUrl))
            {
                await CheckAllAsync(stoppingToken);
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

    public async Task CheckAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var status in registry.All())
        {
            await CheckOneAsync(status, cancellationToken);
        }
    }

    public async Task CheckOneAsync(StaticSiteUpdateStatus status, CancellationToken cancellationToken = default)
    {
        var update = options.CurrentValue.UpdateOptions;
        if (string.IsNullOrWhiteSpace(update.ServerUrl))
        {
            status.Error = "No update server configured (OrbitMesh:UpdateOptions:ServerUrl is empty).";
            return;
        }
        try
        {
            status.CurrentVersion = StaticSiteUpdater.ReadCurrentVersion(status.PhysicalPath);
            var release = await releaseServerClient.GetLatestAsync(update, status.ProjectSlug, status.CurrentVersion, cancellationToken);
            status.LatestVersion = release?.Version;
            status.Changelog = release?.Changelog;
            status.ZipUrl = release?.ZipUrl;
            status.LastCheckedUtc = DateTimeOffset.UtcNow;
            status.Error = null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to check for updates to static site '{Slug}'", status.ProjectSlug);
            status.Error = ex.Message;
        }
    }
}
