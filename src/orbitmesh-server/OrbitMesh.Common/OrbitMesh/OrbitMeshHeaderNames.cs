namespace OrbitMesh;

/// <summary>HTTP header/SignalR connection-option names used to authenticate and identify a
/// connection - internal wire protocol, set automatically by <c>PackageHost</c>.</summary>
public static class OrbitMeshHeaderNames
{
    /// <summary>The credential's access key.</summary>
    public const string AccessKey = "AccessKey";
    /// <summary>The caller's Edge name.</summary>
    public const string EdgeName = "EdgeName";
    /// <summary>The caller's package name.</summary>
    public const string PackageName = "PackageName";
    /// <summary>The SDK version the caller is running.</summary>
    public const string OrbitMeshClientVersion = "OrbitMeshClientVersion";
    /// <summary>The caller's client type/platform (e.g. "net").</summary>
    public const string OrbitMeshClientType = "OrbitMeshClientType";
    /// <summary>The package's own version.</summary>
    public const string PackageVersion = "PackageVersion";
    /// <summary>Whether this connection attempt is a reconnection rather than the first connect.</summary>
    public const string IsReconnection = "IsReconnection";
    /// <summary>A GUID the Edge generates on first run and persists locally - unlike EdgeName, stable
    /// and unique per install. Sent on every connection attempt, authorized or not.</summary>
    public const string InstanceId = "InstanceId";
}
