using Microsoft.Extensions.Options;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using OrbitMesh.Server.Configuration;

namespace OrbitMesh.Server.Services;

public sealed record NuGetFeedPackageSummary(string FeedName, string Id, string Version, string? Description, string? Authors, string? IconUrl, IReadOnlyList<string> Tags);

/// <summary>Reads OrbitMesh packages from the NuGet V3 feeds configured under
/// <see cref="OrbitMeshOptions.NuGetFeeds"/>, via the official NuGet.Protocol client library rather
/// than a hand-rolled HTTP client - correctness (SemVer2 query flags, schema parsing) comes for free,
/// and since this talks to the protocol resources directly (never through `dotnet restore`/MSBuild),
/// none of the CLI-restore-only behavior (NuGetAudit, ambient NuGet.Config, credential plugins)
/// applies here. Read-only: publishing is the packages' own release tooling's job, not the Server's.</summary>
public sealed class NuGetFeedClient(IOptionsMonitor<OrbitMeshOptions> options, Microsoft.Extensions.Logging.ILogger<NuGetFeedClient> logger)
{
    /// <summary>The packageType OrbitMesh apps self-identify as (see the nuspec's &lt;packageTypes&gt;) -
    /// used to filter search results down to OrbitMesh packages on feeds that also host other things.</summary>
    public const string PackageType = "OrbitMeshApp";

    private readonly NuGet.Common.ILogger _nugetLogger = new NuGetLoggerAdapter(logger);

    public async Task<IReadOnlyList<NuGetFeedPackageSummary>> SearchAsync(string searchTerm, bool includePrerelease = false, CancellationToken cancellationToken = default)
    {
        var results = new List<NuGetFeedPackageSummary>();
        using var cacheContext = CreateCacheContext();
        foreach (var feed in EnabledFeeds())
        {
            try
            {
                var repository = BuildRepository(feed);
                var search = await repository.GetResourceAsync<PackageSearchResource>(cancellationToken);
                var filter = new SearchFilter(includePrerelease) { PackageType = PackageType };
                var hits = await search.SearchAsync(searchTerm, filter, skip: 0, take: 100, _nugetLogger, cancellationToken);
                foreach (var hit in hits)
                {
                    results.Add(new NuGetFeedPackageSummary(
                        feed.Name,
                        hit.Identity.Id,
                        hit.Identity.Version.ToNormalizedString(),
                        hit.Description,
                        hit.Authors,
                        hit.IconUrl?.ToString(),
                        string.IsNullOrWhiteSpace(hit.Tags) ? [] : hit.Tags.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
                }
            }
            catch (Exception ex)
            {
                // One misbehaving/unreachable feed shouldn't blank out the others - log and move on.
                logger.LogWarning(ex, "Unable to search NuGet feed '{Feed}'", feed.Name);
            }
        }
        return results;
    }

    public async Task<IReadOnlyList<NuGetVersion>> GetVersionsAsync(string feedName, string packageId, CancellationToken cancellationToken = default)
    {
        var feed = RequireFeed(feedName);
        using var cacheContext = CreateCacheContext();
        var repository = BuildRepository(feed);
        var resource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
        var versions = await resource.GetAllVersionsAsync(packageId, cacheContext, _nugetLogger, cancellationToken);
        return versions.OrderByDescending(v => v).ToList();
    }

    public async Task<bool> DownloadToStreamAsync(string feedName, string packageId, NuGetVersion version, Stream destination, CancellationToken cancellationToken = default)
    {
        var feed = RequireFeed(feedName);
        using var cacheContext = CreateCacheContext();
        var repository = BuildRepository(feed);
        var resource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
        return await resource.CopyNupkgToStreamAsync(packageId, version, destination, cacheContext, _nugetLogger, cancellationToken);
    }

    private IEnumerable<NuGetFeedOptions> EnabledFeeds() => options.CurrentValue.NuGetFeeds.Where(f => f.Enable);

    private NuGetFeedOptions RequireFeed(string feedName) =>
        EnabledFeeds().FirstOrDefault(f => f.Name.Equals(feedName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No enabled NuGet feed named '{feedName}' is configured.");

    private static SourceRepository BuildRepository(NuGetFeedOptions feed)
    {
        var source = new PackageSource(feed.ServiceIndexUrl, feed.Name);
        if (!string.IsNullOrEmpty(feed.ApiKey))
        {
            // Basic auth read: the "password" is an API key, never the account's own password - see
            // the feed server's own auth conventions (e.g. Pépite's).
            source.Credentials = new PackageSourceCredential(feed.Name, feed.Username ?? feed.Name, feed.ApiKey, isPasswordClearText: true, validAuthenticationTypesText: null);
        }
        return Repository.Factory.GetCoreV3(source);
    }

    // NoCache: this runs in a long-lived server process, not a one-shot CLI restore - NuGet.Protocol's
    // default on-disk cache is meant for the latter (re-running `dotnet restore` moments later) and
    // would otherwise serve stale search/version results indefinitely here.
    private static SourceCacheContext CreateCacheContext() => new() { NoCache = true, DirectDownload = true };
}
