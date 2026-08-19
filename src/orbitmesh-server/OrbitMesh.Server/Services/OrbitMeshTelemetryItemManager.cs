using OrbitMesh.Server.Providers;

namespace OrbitMesh.Server.Services;

public sealed class OrbitMeshTelemetryItemManager(ITelemetryItemProvider provider, OrbitMeshMetrics metrics)
{
    public event EventHandler<TelemetryItemUpdatedEventArgs>? TelemetryItemUpdated;

    public void Initialize()
    {
        provider.Open();
        metrics.SetTelemetryItemsCount(provider.Count);
    }

    public void Close() => provider.Close();

    public TelemetryItem PushTelemetryItem(string edgeName, string packageName, string name, object? value, string type = "", Dictionary<string, object>? metadatas = null, int lifetime = 0)
    {
        var telemetryItem = provider.AddOrUpdate(edgeName, packageName, name, value, type, metadatas, lifetime);
        metrics.PushTelemetryItem();
        metrics.SetTelemetryItemsCount(provider.Count);
        TelemetryItemUpdated?.Invoke(null, new TelemetryItemUpdatedEventArgs { TelemetryItem = telemetryItem });
        return telemetryItem;
    }

    public IEnumerable<TelemetryItem> GetTelemetryItems(string edgeName = "*", string packageName = "*", string name = "*", string type = "*") =>
        provider.GetTelemetryItems(edgeName, packageName, name, type);

    public IEnumerable<TelemetryItem> GetTelemetryItemsWithAccessKey(IOrbitMeshDirectory directory, string? accessKey, string edgeName = "*", string packageName = "*", string name = "*", string type = "*")
    {
        if (!directory.CheckTelemetryItemAuthorization(accessKey, edgeName, packageName, name))
        {
            throw new UnauthorizedAccessException("Unauthorized");
        }
        metrics.RequestTelemetryItems();
        return provider.GetTelemetryItems(edgeName, packageName, name, type);
    }

    public void PurgeTelemetryItems(string edgeName, string packageName, string name = "*", string type = "*")
    {
        provider.Remove(edgeName, packageName, name, type);
        metrics.SetTelemetryItemsCount(provider.Count);
    }
}
