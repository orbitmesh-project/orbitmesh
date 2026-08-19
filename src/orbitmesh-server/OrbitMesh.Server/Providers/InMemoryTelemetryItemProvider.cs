using System.Collections.Concurrent;
using System.Text.Json;
using OrbitMesh.Utils;
using Microsoft.Extensions.Logging;

namespace OrbitMesh.Server.Providers;

/// <summary>In-memory telemetry item store with an optional JSON snapshot on disk, restored at startup.</summary>
public sealed class InMemoryTelemetryItemProvider(ILogger<InMemoryTelemetryItemProvider> logger) : ITelemetryItemProvider
{
    private readonly ConcurrentDictionary<string, TelemetryItem> _telemetryItems = new();
    private const string SnapshotFilename = "TelemetryItemsSnapshot.json";

    public int Count => _telemetryItems.Count;

    public void Open()
    {
        if (!File.Exists(SnapshotFilename))
        {
            return;
        }
        try
        {
            var list = JsonSerializer.Deserialize<List<TelemetryItem>>(File.ReadAllText(SnapshotFilename), ObjectConverter.DefaultOptions);
            if (list == null)
            {
                return;
            }
            foreach (var telemetryItem in list)
            {
                _telemetryItems[$"{telemetryItem.EdgeName}/{telemetryItem.PackageName}/{telemetryItem.Name}"] = telemetryItem;
            }
            logger.LogInformation("TelemetryItems snapshot restored ({Count} item(s))", list.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to restore the TelemetryItems snapshot");
        }
    }

    public void Close()
    {
        try
        {
            File.WriteAllText(SnapshotFilename, JsonSerializer.Serialize(_telemetryItems.Values.ToList(), ObjectConverter.DefaultOptions));
            logger.LogInformation("TelemetryItems snapshot saved ({Count} item(s))", _telemetryItems.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to save the TelemetryItems snapshot");
        }
    }

    public IEnumerable<TelemetryItem> GetTelemetryItems(string edgeName = "*", string packageName = "*", string name = "*", string type = "*") =>
        _telemetryItems.Values.Where(so =>
            (edgeName == "*" || edgeName.Equals(so.EdgeName, StringComparison.OrdinalIgnoreCase))
            && (packageName == "*" || packageName.Equals(so.PackageName, StringComparison.OrdinalIgnoreCase))
            && (name == "*" || name.Equals(so.Name, StringComparison.OrdinalIgnoreCase))
            && (type == "*" || type.Equals(so.Type, StringComparison.OrdinalIgnoreCase)));

    public TelemetryItem AddOrUpdate(string edgeName, string packageName, string name, object? value, string type = "", Dictionary<string, object>? metadatas = null, int lifetime = 0)
    {
        var telemetryItem = new TelemetryItem
        {
            LastUpdate = DateTime.Now,
            EdgeName = edgeName,
            Name = name,
            PackageName = packageName,
            Value = value,
            Type = type,
            Metadatas = metadatas,
            Lifetime = lifetime
        };
        _telemetryItems[$"{edgeName}/{packageName}/{name}"] = telemetryItem;
        return telemetryItem;
    }

    public int Remove(string edgeName, string packageName, string name = "*", string type = "*")
    {
        var keys = name == "*"
            ? _telemetryItems.Values
                .Where(so => so.EdgeName == edgeName && so.PackageName == packageName
                    && (type == "*" || type.Equals(so.Type, StringComparison.OrdinalIgnoreCase)))
                .Select(so => $"{so.EdgeName}/{so.PackageName}/{so.Name}")
                .ToList()
            : new List<string> { $"{edgeName}/{packageName}/{name}" };

        var removed = 0;
        foreach (var key in keys)
        {
            if (_telemetryItems.TryRemove(key, out _))
            {
                removed++;
            }
        }
        return removed;
    }
}
