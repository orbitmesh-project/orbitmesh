using System.Collections.Concurrent;
using OrbitMesh.Server.Configuration;
using Microsoft.Extensions.Options;
using FileServerOptions = OrbitMesh.Server.Configuration.FileServerOptions;

namespace OrbitMesh.Server.Services;

/// <summary>Keeps one <see cref="StaticSiteUpdateStatus"/> per <c>FileServerOptions</c> entry that
/// has an <c>UpdateProjectSlug</c> configured, kept in sync with config the same way
/// <see cref="PackageRegistrySync"/> keeps the package registry in sync - a slug added, removed, or
/// renamed via the console's raw config editor takes effect without a restart.</summary>
public sealed class StaticSiteUpdateRegistry(IOptionsMonitor<OrbitMeshOptions> options)
{
    private readonly ConcurrentDictionary<string, StaticSiteUpdateStatus> _bySlug = new(StringComparer.OrdinalIgnoreCase);

    public void Sync()
    {
        var configured = options.CurrentValue.FileServers
            .Where(fs => !string.IsNullOrWhiteSpace(fs.UpdateProjectSlug))
            .ToDictionary(fs => fs.UpdateProjectSlug!, fs => fs, StringComparer.OrdinalIgnoreCase);

        foreach (var staleSlug in _bySlug.Keys.Except(configured.Keys, StringComparer.OrdinalIgnoreCase).ToList())
        {
            _bySlug.TryRemove(staleSlug, out _);
        }

        foreach (var (slug, fileServer) in configured)
        {
            // Only (re)create the entry when it's new or its target actually moved - Sync() runs on
            // every config change, and replacing it unconditionally would throw away accumulated
            // check results (LatestVersion, LastCheckedUtc, ...) over something unrelated, like an
            // admin editing a credential.
            if (_bySlug.TryGetValue(slug, out var existing)
                && existing.Path == fileServer.Path
                && existing.PhysicalPath == fileServer.PhysicalPath)
            {
                continue;
            }
            _bySlug[slug] = new StaticSiteUpdateStatus(fileServer.Path, fileServer.PhysicalPath, slug);
        }
    }

    public IReadOnlyCollection<StaticSiteUpdateStatus> All() => _bySlug.Values.ToList();

    public StaticSiteUpdateStatus? Find(string slug) => _bySlug.GetValueOrDefault(slug);

    public FileServerOptions? FindFileServer(string slug) =>
        options.CurrentValue.FileServers.FirstOrDefault(fs =>
            string.Equals(fs.UpdateProjectSlug, slug, StringComparison.OrdinalIgnoreCase));
}
