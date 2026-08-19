namespace OrbitMesh;

/// <summary>A remote control action the Console/Server can send to a running package instance.</summary>
public enum PackageControlAction
{
    /// <summary>Launch the package.</summary>
    Start,
    /// <summary>Stop the package.</summary>
    Stop,
    /// <summary>Stop then relaunch the package.</summary>
    Restart,
    /// <summary>Stop then relaunch the package (identical handling to <see cref="Restart"/> on the receiving side).</summary>
    Reload
}
