using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrbitMesh.Updating;

/// <summary>Shared "what's the latest release" call against a ci4-updater-server-shaped update
/// server (GET /api/{slug}/latest.json), used by the Server's own update check, the Edge's, and the
/// static site (Console, or any other) update check - same wire format, same auth, only the slug differs.</summary>
public sealed class ReleaseServerClient(IHttpClientFactory httpClientFactory)
{
    // Matches the ci4-updater-server wire format (version/changelog/zip_url, snake_case) without
    // per-property JsonPropertyName attributes - same trick as SpotifyClient's outbound API calls.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>Fetches the latest release info for <paramref name="projectSlug"/> from the update
    /// server described by <paramref name="update"/>, optionally telling the server the caller's
    /// current version (<paramref name="fromVersion"/>) so it can include a changelog since then.</summary>
    public async Task<LatestRelease?> GetLatestAsync(UpdateOptions update, string projectSlug, string? fromVersion, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(nameof(ReleaseServerClient));
        var url = $"{update.ServerUrl.TrimEnd('/')}/api/{projectSlug}/latest.json";
        if (!string.IsNullOrEmpty(fromVersion))
        {
            url += $"?from={Uri.EscapeDataString(fromVersion)}";
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(update.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", update.Token);
        }
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LatestRelease>(JsonOptions, cancellationToken);
    }

    /// <summary>The update server's response describing the latest available release.</summary>
    public sealed class LatestRelease
    {
        /// <summary>The latest version available.</summary>
        public string? Version { get; set; }

        /// <summary>Release notes, typically since the caller's <c>fromVersion</c> if it was provided.</summary>
        public string? Changelog { get; set; }

        /// <summary>URL to download the release's zip from.</summary>
        public string? ZipUrl { get; set; }
    }
}
