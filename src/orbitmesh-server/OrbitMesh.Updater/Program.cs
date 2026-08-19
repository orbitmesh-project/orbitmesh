using OrbitMesh.Updater;
using OrbitMesh.Updating;

var log = new UpdateLog(AppContext.BaseDirectory);

UpdateHandoffArgs handoff;
try
{
    handoff = UpdateHandoffArgs.Parse(args);
}
catch (Exception ex)
{
    log.Error($"Invalid arguments: {ex.Message}");
    return 1;
}

log.Info($"Waiting for PID {handoff.CallerPid} to exit...");
if (!await ProcessWaiter.WaitForExitAsync(handoff.CallerPid, TimeSpan.FromSeconds(60)))
{
    log.Error($"PID {handoff.CallerPid} did not exit within the timeout - aborting the update, nothing was touched.");
    return 1;
}

log.Info("Caller has exited. Swapping files...");
string backupDirectory;
try
{
    backupDirectory = FileSwapper.Swap(handoff.LiveDirectory, handoff.StagingDirectory);
}
catch (Exception ex)
{
    log.Error($"File swap failed: {ex.Message}");
    return 1;
}

log.Info($"Files swapped. Restarting via {handoff.RestartMode}...");
int? standaloneProcessId;
try
{
    standaloneProcessId = ProcessRestarter.Restart(handoff);
}
catch (Exception ex)
{
    log.Error($"Restart failed: {ex.Message} - rolling back to the previous version.");
    FileSwapper.Rollback(handoff.LiveDirectory, backupDirectory);
    ProcessRestarter.Restart(handoff);
    return 1;
}

if (string.IsNullOrEmpty(handoff.HealthCheckUrl))
{
    log.Info("Update applied (no health check configured).");
    return 0;
}

log.Info($"Waiting up to {handoff.HealthCheckTimeoutSeconds}s for {handoff.HealthCheckUrl} to report healthy...");
if (await HealthChecker.WaitUntilHealthyAsync(handoff.HealthCheckUrl, TimeSpan.FromSeconds(handoff.HealthCheckTimeoutSeconds)))
{
    log.Info("New version is healthy. Update complete.");
    return 0;
}

log.Error("New version failed its health check - rolling back to the previous version.");
// The new version is still running at this point - its own files are open, so the live directory
// can't even be deleted (let alone replaced) until it's stopped.
ProcessRestarter.Stop(handoff, standaloneProcessId);
FileSwapper.Rollback(handoff.LiveDirectory, backupDirectory);
ProcessRestarter.Restart(handoff);
log.Info("Rollback complete - previous version relaunched.");
return 1;
