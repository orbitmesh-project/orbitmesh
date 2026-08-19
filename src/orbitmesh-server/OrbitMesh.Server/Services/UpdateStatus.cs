using OrbitMesh.Updating;

namespace OrbitMesh.Server.Services;

/// <summary>Latest known result of checking the configured update server, shared between
/// <see cref="UpdateCheckService"/> and the Management API. A plain mutable singleton is enough here -
/// there is only ever one writer, and readers don't need a consistent snapshot across properties.</summary>
public sealed class UpdateStatus
{
    public string CurrentVersion { get; set; } = string.Empty;

    public string? LatestVersion { get; set; }

    public string? Changelog { get; set; }

    public string? ZipUrl { get; set; }

    public DateTimeOffset? LastCheckedUtc { get; set; }

    public string? Error { get; set; }

    public bool IsUpdateAvailable => UpdateVersionComparer.IsNewer(LatestVersion, CurrentVersion);

    // OrbitMesh.Updater (see ServerSelfUpdater.ApplyAsync) only ever touches files after this process
    // has fully exited, so the swap works the same way on every OS - unlike the in-process swap this
    // used to do, which relied on a Linux-only "open file survives a rename" trick.
    public bool CanAutoApply => true;
}
