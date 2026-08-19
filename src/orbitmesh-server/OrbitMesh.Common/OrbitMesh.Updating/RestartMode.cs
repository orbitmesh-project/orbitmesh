namespace OrbitMesh.Updating;

/// <summary>How OrbitMesh.Updater should bring the target process back up once the file swap is done -
/// each hosting mode needs a different restart mechanism, and Updater can't guess which one applies.</summary>
public enum RestartMode
{
    /// <summary>No service manager involved - Updater relaunches the executable directly.</summary>
    Standalone,

    /// <summary>Restarted via the Windows Service Control Manager (<c>ServiceOrUnitName</c> is the service name).</summary>
    WindowsService,

    /// <summary>Restarted via <c>systemctl start</c> (<c>ServiceOrUnitName</c> is the unit name).</summary>
    Systemd
}
