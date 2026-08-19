namespace OrbitMesh.Package;

/// <summary>Raised when a tracked telemetry item's value changes - see
/// <see cref="TelemetryItemCollectionNotifier"/>.</summary>
public sealed class TelemetryItemChangedEventArgs : EventArgs
{
    /// <summary>The item's previous state, or null if this is the first value seen.</summary>
    public TelemetryItem? OldState { get; set; }

    /// <summary>The item's current state.</summary>
    public TelemetryItem? NewState { get; set; }

    /// <summary>True if this is the first value seen for this item (<see cref="OldState"/> is null).</summary>
    public bool IsNew => OldState == null;
}
