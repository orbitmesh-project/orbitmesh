namespace OrbitMesh.Updater;

internal static class HealthChecker
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    // Any HTTP response at all - not specifically a 2xx - counts as healthy: the endpoints this polls
    // (e.g. the Server's /rest/management/server/version) are behind an AccessKey gate that Updater
    // has no reason to be handed just to run a liveness probe, so a 403 there is actually proof the
    // new process is up and answering requests, which is exactly what's being verified.
    public static async Task<bool> WaitUntilHealthyAsync(string url, TimeSpan timeout)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync(url);
                return true;
            }
            catch
            {
                // Not up yet (connection refused, still starting, ...) - keep polling until the deadline.
            }
            await Task.Delay(PollInterval);
        }
        return false;
    }
}
