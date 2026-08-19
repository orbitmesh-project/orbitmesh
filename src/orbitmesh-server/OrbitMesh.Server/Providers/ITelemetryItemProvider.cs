namespace OrbitMesh.Server.Providers;

/// <summary>Storage backend for telemetry items. Swap in a different implementation (e.g. SQLite) via DI if needed.</summary>
public interface ITelemetryItemProvider
{
    int Count { get; }

    void Open();

    void Close();

    IEnumerable<TelemetryItem> GetTelemetryItems(string edgeName = "*", string packageName = "*", string name = "*", string type = "*");

    TelemetryItem AddOrUpdate(string edgeName, string packageName, string name, object? value, string type = "", Dictionary<string, object>? metadatas = null, int lifetime = 0);

    int Remove(string edgeName, string packageName, string name = "*", string type = "*");
}
