namespace OrbitMesh;

/// <summary>Event args for <c>PackageHost.TelemetryItemUpdated</c>.</summary>
public sealed class TelemetryItemUpdatedEventArgs : EventArgs
{
    /// <summary>The item's new state.</summary>
    public required TelemetryItem TelemetryItem { get; set; }
}
