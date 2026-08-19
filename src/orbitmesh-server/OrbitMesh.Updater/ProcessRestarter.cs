using System.Diagnostics;
using OrbitMesh.Updating;

namespace OrbitMesh.Updater;

internal static class ProcessRestarter
{
    /// <summary>Starts the target process/service. Returns the started PID for RestartMode.Standalone
    /// (needed by <see cref="Stop"/> to roll it back if the health check fails - a service/unit is
    /// stopped by name instead, so null is returned for those modes).</summary>
    public static int? Restart(UpdateHandoffArgs handoff)
    {
        switch (handoff.RestartMode)
        {
            case RestartMode.Standalone:
                return RestartStandalone(handoff);
            case RestartMode.WindowsService:
                RunToolCommand("sc.exe", ["start", RequireServiceOrUnitName(handoff)]);
                return null;
            case RestartMode.Systemd:
                // "sudo" - the service (and this process, its child) runs as a non-root user; a sudoers
                // rule scoped to exactly this command is set up by install.sh.
                RunToolCommand("sudo", ["systemctl", "start", RequireServiceOrUnitName(handoff)]);
                return null;
            default:
                throw new NotSupportedException($"Unknown restart mode '{handoff.RestartMode}'.");
        }
    }

    /// <summary>Stops whatever <see cref="Restart"/> just started - required before a rollback can
    /// delete the live directory: a still-running process (or service) keeps its own files open, so
    /// FileSwapper.Rollback would otherwise fail to even remove that directory, let alone replace it.</summary>
    public static void Stop(UpdateHandoffArgs handoff, int? standaloneProcessId)
    {
        switch (handoff.RestartMode)
        {
            case RestartMode.Standalone:
                StopStandalone(standaloneProcessId);
                break;
            case RestartMode.WindowsService:
                RunToolCommand("sc.exe", ["stop", RequireServiceOrUnitName(handoff)]);
                break;
            case RestartMode.Systemd:
                RunToolCommand("sudo", ["systemctl", "stop", RequireServiceOrUnitName(handoff)]);
                break;
        }
    }

    private static int? RestartStandalone(UpdateHandoffArgs handoff)
    {
        if (string.IsNullOrEmpty(handoff.RestartExecutable))
        {
            throw new InvalidOperationException("RestartExecutable is required for Standalone restarts.");
        }
        var startInfo = new ProcessStartInfo(handoff.RestartExecutable)
        {
            UseShellExecute = false,
            WorkingDirectory = handoff.RestartWorkingDirectory ?? handoff.LiveDirectory
        };
        foreach (var arg in handoff.RestartArguments)
        {
            startInfo.ArgumentList.Add(arg);
        }
        // Deliberately not disposed - this is a fresh, independent, long-running process that must
        // keep running after Updater itself exits (unless Stop() above tears it down for a rollback).
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{handoff.RestartExecutable}'.");
        return process.Id;
    }

    private static void StopStandalone(int? processId)
    {
        if (processId == null)
        {
            return;
        }
        try
        {
            using var process = Process.GetProcessById(processId.Value);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(10_000);
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }

    private static string RequireServiceOrUnitName(UpdateHandoffArgs handoff) =>
        handoff.ServiceOrUnitName ?? throw new InvalidOperationException($"ServiceOrUnitName is required for {handoff.RestartMode} restarts.");

    private static void RunToolCommand(string fileName, string[] args)
    {
        var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = false };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"'{fileName} {string.Join(' ', args)}' exited with code {process.ExitCode}.");
        }
    }
}
