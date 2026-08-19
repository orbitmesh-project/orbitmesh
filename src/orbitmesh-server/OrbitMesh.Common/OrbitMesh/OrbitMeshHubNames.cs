namespace OrbitMesh;

/// <summary>The Server's SignalR hub route segments, one per connection kind.</summary>
public static class OrbitMeshHubNames
{
    /// <summary>The hub packages connect to (<c>PackageHost</c>'s own connection) - messages, telemetry, settings.</summary>
    public const string OrbitMesh = "OrbitMeshHub";
    /// <summary>The hub the Console connects to - live edge/package state, logs, remote control.</summary>
    public const string Control = "ControlHub";
    /// <summary>The hub external read-only consumers connect to.</summary>
    public const string Consumer = "ConsumerHub";
    /// <summary>The hub Edge processes connect to - registration, package list, remote control.</summary>
    public const string Edge = "EdgeHub";
}
