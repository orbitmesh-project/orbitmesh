using System.Net.Http.Headers;

namespace OrbitMesh.Updating;

/// <summary>Downloads a release .zip to a temp file - shared by the Server's, and the Edge's, self-update
/// orchestrators, plus <c>StaticSiteUpdater</c>, which all differ only in what they do with the extracted
/// contents.</summary>
public static class ReleaseZipDownloader
{
    // The update server's file endpoint enforces the same per-project Bearer token as its latest.json
    // endpoint (see ReleaseServerClient) - a release that requires auth to even learn about needs the
    // same auth to actually fetch, so the token has to travel here too, not just on the check call.
    /// <summary>Downloads <paramref name="zipUrl"/> to a uniquely-named temp file and returns its
    /// path. <paramref name="clientName"/> selects the named <see cref="HttpClient"/> to use (see
    /// <see cref="IHttpClientFactory"/>); <paramref name="bearerToken"/> is sent as a Bearer
    /// Authorization header if given.</summary>
    public static async Task<string> DownloadToTempFileAsync(IHttpClientFactory httpClientFactory, string clientName, string zipUrl, string? bearerToken, CancellationToken cancellationToken)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"orbitmesh-update-{Guid.NewGuid():N}.zip");
        var client = httpClientFactory.CreateClient(clientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, zipUrl);
        if (!string.IsNullOrEmpty(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var file = File.Create(zipPath);
        await stream.CopyToAsync(file, cancellationToken);
        return zipPath;
    }
}
