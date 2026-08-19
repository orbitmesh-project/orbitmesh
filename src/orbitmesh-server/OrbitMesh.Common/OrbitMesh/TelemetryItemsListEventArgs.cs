namespace OrbitMesh;

/// <summary>Event args for <c>PackageHost.LastTelemetryItemsReceived</c>.</summary>
public sealed class TelemetryItemsListEventArgs : EventArgs
{
    /// <summary>The current values of every telemetry item matching the originating
    /// <c>PackageHost.RequestTelemetryItems</c> call.</summary>
    public required List<TelemetryItem> TelemetryItems { get; set; }
}
