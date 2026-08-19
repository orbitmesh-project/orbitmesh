namespace OrbitMesh;

/// <summary>Method names clients invoke on OrbitMeshHub/ConsumerHub - internal wire protocol, not
/// meant to be called directly by package authors (use the corresponding <c>PackageHost</c> method).</summary>
public static class OrbitMeshServerMethodNames
{
    /// <summary>Requests the caller's current settings.</summary>
    public const string RequestSettings = "RequestSettings";
    /// <summary>Writes a log line.</summary>
    public const string WriteLog = "WriteLog";
    /// <summary>Sends a message.</summary>
    public const string SendMessage = "SendMessage";
    /// <summary>Publishes a telemetry item value.</summary>
    public const string PushTelemetryItem = "PushTelemetryItem";
    /// <summary>Deletes telemetry items.</summary>
    public const string PurgeTelemetryItems = "PurgeTelemetryItems";
    /// <summary>Requests the current value of matching telemetry items.</summary>
    public const string RequestTelemetryItems = "RequestTelemetryItems";
    /// <summary>Subscribes to future updates for matching telemetry items.</summary>
    public const string SubscribeTelemetryItems = "SubscribeTelemetryItems";
    /// <summary>Undoes a prior subscription to matching telemetry items.</summary>
    public const string UnSubscribeTelemetryItems = "UnSubscribeTelemetryItems";
    /// <summary>Joins a named message group.</summary>
    public const string SubscribeMessages = "SubscribeMessages";
    /// <summary>Leaves a named message group.</summary>
    public const string UnSubscribeMessages = "UnSubscribeMessages";
    /// <summary>Declares/updates the caller's <see cref="PackageDescriptor"/>.</summary>
    public const string DeclarePackageDescriptor = "DeclarePackageDescriptor";
}
