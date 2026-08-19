using System.Diagnostics;

namespace OrbitMesh.Edge.Utils;

/// <summary>
/// CPU/RAM sampling for a monitored package process. Modern .NET's <see cref="Process.TotalProcessorTime"/>
/// is accurate cross-platform, so unlike the original there is no Windows/Linux branch (no WMI, no manual
/// <c>/proc/stat</c> parsing).
/// </summary>
public sealed class ProcessMonitor
{
    private readonly Process _process;
    private readonly TimeSpan _cpuTimeOnStart;
    private TimeSpan _lastCpuTime;
    private DateTime _lastSampleTime = DateTime.UtcNow;

    public ProcessMonitor(Process process)
    {
        _process = process;
        _cpuTimeOnStart = process.TotalProcessorTime;
    }

    public double GetCpuUsagePercent()
    {
        _process.Refresh();
        var elapsedCpuTime = _process.TotalProcessorTime - _cpuTimeOnStart;
        var deltaCpuTime = elapsedCpuTime - _lastCpuTime;
        var deltaWallTime = DateTime.UtcNow - _lastSampleTime;
        _lastSampleTime = DateTime.UtcNow;
        _lastCpuTime = elapsedCpuTime;

        if (deltaWallTime <= TimeSpan.Zero)
        {
            return 0;
        }
        var usage = deltaCpuTime.TotalSeconds / (Environment.ProcessorCount * deltaWallTime.TotalSeconds);
        return Math.Round(usage * 100.0, 2);
    }

    public long GetWorkingSet()
    {
        _process.Refresh();
        return _process.WorkingSet64;
    }
}
