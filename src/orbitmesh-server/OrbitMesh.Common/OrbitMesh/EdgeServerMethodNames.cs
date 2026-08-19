namespace OrbitMesh;

/// <summary>Method names the Edge invokes on the Edge hub - internal wire protocol.</summary>
public static class EdgeServerMethodNames
{
    /// <summary>Asks the Server for its version.</summary>
    public const string GetServerVersion = "GetServerVersion";
    /// <summary>Registers this Edge with the Server on connect.</summary>
    public const string RegisterEdge = "RegisterEdge";
    /// <summary>Reports a package instance's current state.</summary>
    public const string ReportPackageState = "ReportPackageState";
    /// <summary>Reports CPU/RAM usage for the Edge's running packages.</summary>
    public const string ReportPackagesUsage = "ReportPackagesUsage";
    /// <summary>Asks the Server to restart this Edge.</summary>
    public const string RestartEdge = "RestartEdge";
    /// <summary>Asks the Edge to check for and apply an update now, instead of waiting for its own timer.</summary>
    public const string CheckForUpdate = "CheckForUpdate";
    /// <summary>Writes a log line on behalf of a package this Edge hosts.</summary>
    public const string WriteLog = "WriteLog";
}
