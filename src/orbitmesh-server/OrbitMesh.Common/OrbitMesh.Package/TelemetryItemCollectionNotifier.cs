using System.Collections.ObjectModel;

namespace OrbitMesh.Package;

/// <summary>A live, filterable collection of tracked telemetry items, keyed by their unique id. Not
/// used directly by most packages - <see cref="PackageHost.RegisterTelemetryItemCallback"/> and
/// <see cref="TelemetryItemLinkAttribute"/> are the usual entry points; this is what backs them.</summary>
public class TelemetryItemCollectionNotifier : ObservableCollection<TelemetryItemNotifier>
{
    /// <summary>Looks up (or, on set, adds/updates) the item with this unique id
    /// (see <see cref="TelemetryItem.UniqueId"/>).</summary>
    public TelemetryItemNotifier? this[string telemetryItemId]
    {
        get => this.FirstOrDefault(s => s.Value?.UniqueId == telemetryItemId);
        set
        {
            if (value?.Value != null)
            {
                AddOrUpdate(value.Value);
            }
        }
    }

    /// <summary>Returns the subset matching the given edge/package/name/type ("*" matches anything).</summary>
    public ObservableCollection<TelemetryItemNotifier> Filter(string edge = "*", string package = "*", string name = "*", string type = "*") =>
        new(this.Where(so => Matches(so, edge, package, name, type)));

    /// <summary>Raised whenever any tracked item's value changes.</summary>
    public event EventHandler<TelemetryItemChangedEventArgs>? ValueChanged;

    /// <summary>True if an item with this unique id is currently tracked.</summary>
    public bool ContainsTelemetryItem(string telemetryItemId) => this[telemetryItemId] != null;

    /// <summary>True if any tracked item matches the given edge/package/name/type ("*" matches anything).</summary>
    public bool ContainsTelemetryItem(string edge = "*", string package = "*", string name = "*", string type = "*") =>
        this.Any(so => Matches(so, edge, package, name, type));

    /// <summary>Adds a new item, or updates the existing one with the same unique id, raising
    /// <see cref="ValueChanged"/> either way.</summary>
    public void AddOrUpdate(TelemetryItem telemetryItem)
    {
        TelemetryItem? oldState = null;
        var notifier = this[telemetryItem.UniqueId];
        if (notifier == null)
        {
            Add(new TelemetryItemNotifier { Value = telemetryItem });
        }
        else
        {
            oldState = notifier.Value;
            notifier.Value = telemetryItem;
        }
        ValueChanged?.Invoke(this, new TelemetryItemChangedEventArgs { OldState = oldState, NewState = telemetryItem });
    }

    /// <summary>Adds/updates each item in turn - see <see cref="AddOrUpdate(TelemetryItem)"/>.</summary>
    public void AddOrUpdate(IEnumerable<TelemetryItem> telemetryItems)
    {
        foreach (var telemetryItem in telemetryItems)
        {
            AddOrUpdate(telemetryItem);
        }
    }

    private static bool Matches(TelemetryItemNotifier so, string edge, string package, string name, string type) =>
        so.Value != null
        && (edge == "*" || edge.Equals(so.Value.EdgeName, StringComparison.OrdinalIgnoreCase))
        && (package == "*" || package.Equals(so.Value.PackageName, StringComparison.OrdinalIgnoreCase))
        && (name == "*" || name.Equals(so.Value.Name, StringComparison.OrdinalIgnoreCase))
        && (type == "*" || type.Equals(so.Value.Type, StringComparison.OrdinalIgnoreCase));
}
