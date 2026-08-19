namespace OrbitMesh;

/// <summary>A package instance's current lifecycle state, as tracked by the Edge/Server and shown in
/// the Console.</summary>
public enum PackageState
{
    /// <summary>State hasn't been reported yet.</summary>
    Unknown,
    /// <summary>Shutting down.</summary>
    Stopping,
    /// <summary>Not running.</summary>
    Stopped,
    /// <summary>Process launched, not yet connected/ready.</summary>
    Starting,
    /// <summary>Running and connected.</summary>
    Started
}
