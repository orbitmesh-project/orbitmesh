namespace OrbitMesh.Updating;

/// <summary>Self-update checking against an external release server (e.g. a ci4-updater-server
/// instance exposing GET /api/{slug}/latest.json). Empty ServerUrl - the default - disables the
/// feature entirely: no background checks, no update notification in the console. Shared shape
/// between the Server's and the Edge's own update sections - they only ever differ in ProjectSlug
/// and section name in appsettings.json.</summary>
public sealed class UpdateOptions
{
    /// <summary>The update server's base URL. Empty (the default) disables self-update checking entirely.</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>This project's slug on the update server, e.g. "orbitmesh-server".</summary>
    public string ProjectSlug { get; set; } = "orbitmesh-server";

    /// <summary>Optional auth token sent to the update server.</summary>
    public string? Token { get; set; }

    /// <summary>How often to poll for updates, in minutes.</summary>
    public int CheckIntervalMinutes { get; set; } = 60;

    /// <summary>Public keys (PEM contents, or paths relative to the app's own directory) trusted
    /// to sign releases. Empty - the default - means signatures aren't checked at all, same as
    /// ci4-updater's own default: updates are trusted on the strength of the connection to the
    /// update server. As soon as one key is listed, every release must carry a manifest.json and a
    /// valid manifest.json.sig signed by one of these keys, or it's refused outright.</summary>
    public List<string> PublicKeys { get; set; } = new();

    /// <summary>Windows Service name, or systemd unit name, this instance runs as - required only
    /// when it actually runs hosted that way (OrbitMesh.Updater needs it to know how to restart the
    /// process after swapping files). Unused when running standalone (e.g. local development).</summary>
    public string? ServiceOrUnitName { get; set; }

    /// <summary>Path to OrbitMesh.Updater.dll. Empty - the default - resolves it as a sibling "updater"
    /// folder next to this app's own install directory, where it's expected to be deployed once
    /// (outside any directory an update would ever swap out from under its own running process).</summary>
    public string? UpdaterExecutablePath { get; set; }
}
