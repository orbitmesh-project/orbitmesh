namespace OrbitMesh;

/// <summary>Reserved names with special meaning in the OrbitMesh protocol.</summary>
public static class OrbitMeshDefaultNames
{
    /// <summary>Matches anything, used throughout the telemetry/message filter APIs.</summary>
    public const string Wildcard = "*";
    /// <summary>The pseudo-package-name representing the Edge process itself, not one of its packages.</summary>
    public const string EdgePackageName = "Edge";
    /// <summary>The pseudo-edge-name used by the Console's admin connections.</summary>
    public const string ControllerEdgeName = "Controller";
    /// <summary>The pseudo-edge-name used by external read-only consumer connections.</summary>
    public const string ConsumerEdgeName = "Consumer";
    /// <summary>The pseudo-edge-name for a package run standalone/directly by a developer, with no real Edge.</summary>
    public const string DeveloperEdgeName = "Developer";
    /// <summary>The pseudo-edge-name used by Management REST API calls.</summary>
    public const string ManagementEdgeName = "Management";
    /// <summary>The message key a saga (request/response) response is sent under.</summary>
    public const string SagaResponseMessageKey = "__Response";
}
