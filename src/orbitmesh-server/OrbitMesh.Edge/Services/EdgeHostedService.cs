using Microsoft.Extensions.Hosting;

namespace OrbitMesh.Edge.Services;

public sealed class EdgeHostedService(EdgeManager manager) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await manager.StartAsync(stoppingToken);
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await manager.StopAsync();
        await base.StopAsync(cancellationToken);
    }
}
