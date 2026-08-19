namespace OrbitMesh;

/// <summary>Method names the server invokes on OrbitMeshHub/ConsumerHub clients - internal wire
/// protocol, handled automatically by <c>PackageHost</c>.</summary>
public static class OrbitMeshClientMethodNames
{
    /// <summary>A remote control action (start/stop/restart) for this package.</summary>
    public const string PackageControlAction = "PackageControlAction";
    /// <summary>Pushes updated settings.</summary>
    public const string UpdateSettings = "UpdateSettings";
    /// <summary>Delivers the current values for a telemetry item request.</summary>
    public const string ReceiveLastTelemetryItems = "ReceiveLastTelemetryItems";
    /// <summary>Notifies of a subscribed telemetry item's new value.</summary>
    public const string UpdateTelemetryItem = "UpdateTelemetryItem";
    /// <summary>Delivers an incoming message.</summary>
    public const string ReceiveMessage = "ReceiveMessage";
}
