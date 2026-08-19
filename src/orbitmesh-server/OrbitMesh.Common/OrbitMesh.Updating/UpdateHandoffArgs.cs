using System.Text;
using System.Text.Json;

namespace OrbitMesh.Updating;

/// <summary>Everything OrbitMesh.Updater needs to finish an update, handed off entirely through the
/// command line - never a temp file, so there's nothing left on disk describing an in-progress update if
/// Updater itself never gets a chance to clean up. Serialized as a single base64-encoded JSON blob (one
/// argv element) rather than several <c>--flag value</c> pairs, so quoting/escaping rules never have to
/// be reasoned about across Windows/Linux argv parsing - it's opaque to the shell either way.</summary>
public sealed record UpdateHandoffArgs
{
    /// <summary>PID of the process hand this update off. Updater waits for this PID to fully exit
    /// (<c>Process.WaitForExit</c>) before touching any files - the only cross-platform way to know the
    /// old files are actually released, without relying on OS-specific "rename an open file" tricks.</summary>
    public required int CallerPid { get; init; }

    /// <summary>The live install directory to update (backed up, then replaced by StagingDirectory).</summary>
    public required string LiveDirectory { get; init; }

    /// <summary>Already-downloaded, already-verified, already-extracted release contents (see
    /// ReleaseZipDownloader/ReleaseVerifier) - Updater only ever moves this into place, it never
    /// downloads or verifies anything itself.</summary>
    public required string StagingDirectory { get; init; }

    /// <summary>How to relaunch the process after the update - as a Windows service, a systemd unit,
    /// or a standalone process Updater starts directly.</summary>
    public required RestartMode RestartMode { get; init; }

    /// <summary>Service or systemd unit name - required for RestartMode.WindowsService/Systemd, unused
    /// for Standalone.</summary>
    public string? ServiceOrUnitName { get; init; }

    /// <summary>Executable to relaunch in Standalone mode (e.g. "dotnet", or a self-contained exe path).</summary>
    public string? RestartExecutable { get; init; }

    /// <summary>Arguments to relaunch with in Standalone mode (e.g. the server/edge dll path plus its
    /// own original argv) - captured from the caller's own launch so Updater doesn't need to know
    /// anything about what those arguments mean.</summary>
    public IReadOnlyList<string> RestartArguments { get; init; } = [];

    /// <summary>Working directory for the relaunched Standalone process. Defaults to LiveDirectory
    /// when null.</summary>
    public string? RestartWorkingDirectory { get; init; }

    /// <summary>URL Updater polls after restarting to confirm the new version actually came up healthy.
    /// Null skips the health check (and therefore rollback) entirely.</summary>
    public string? HealthCheckUrl { get; init; }

    /// <summary>How long to wait for <see cref="HealthCheckUrl"/> to respond healthy before rolling back.</summary>
    public int HealthCheckTimeoutSeconds { get; init; } = 30;

    /// <summary>Encodes this instance as the single argv element Updater is launched with.</summary>
    public string[] ToArgv() => [Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this)))];

    /// <summary>Decodes the argv Updater was launched with back into an instance - the inverse of <see cref="ToArgv"/>.</summary>
    public static UpdateHandoffArgs Parse(string[] args)
    {
        if (args.Length != 1)
        {
            throw new ArgumentException($"Expected exactly one argument (a base64-encoded {nameof(UpdateHandoffArgs)} blob), got {args.Length}.");
        }
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(args[0]));
        return JsonSerializer.Deserialize<UpdateHandoffArgs>(json)
            ?? throw new ArgumentException("The handoff argument did not deserialize to a valid UpdateHandoffArgs.");
    }
}
