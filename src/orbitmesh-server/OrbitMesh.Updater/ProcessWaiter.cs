using System.Diagnostics;

namespace OrbitMesh.Updater;

internal static class ProcessWaiter
{
    /// <summary>Waits for the given PID to exit. Returns true once it's gone (including if it was
    /// already gone before this was even called) or false if it's still running past the timeout.</summary>
    public static async Task<bool> WaitForExitAsync(int pid, TimeSpan timeout)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            return true;
        }

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
