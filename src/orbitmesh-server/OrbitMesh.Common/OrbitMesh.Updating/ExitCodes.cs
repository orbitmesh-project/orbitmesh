namespace OrbitMesh.Updating;

/// <summary>Well-known process exit codes used across the self-update handoff.</summary>
public static class ExitCodes
{
    /// <summary>The process is exiting on purpose because it just handed off to OrbitMesh.Updater, which
    /// owns the restart from here. Deliberately a clean exit(0), not a distinct nonzero code: systemd's
    /// <c>Restart=on-failure</c> already skips restarting on a clean exit(0) (the same behavior a plain
    /// "systemctl stop" relies on), so this needs no restart-policy exception of its own on Linux. On
    /// Windows, the Service Control Manager still requires <c>sc.exe failureflag &lt;name&gt; 1</c> to be
    /// set once at install time for the equivalent "don't treat this stop as a failure" behavior.</summary>
    public const int UpdatePending = 0;
}
