namespace OrbitMesh.Server;

public static class HttpRequestExtensions
{
    /// <summary>Reads a value from the request header first, falling back to the query string - every
    /// REST/hub entry point in this app accepts EdgeName/PackageName/AccessKey either way.</summary>
    public static string? GetHeaderOrQuery(this HttpRequest request, string key) =>
        request.Headers.TryGetValue(key, out var header) ? header.ToString() : request.Query.TryGetValue(key, out var query) ? query.ToString() : null;
}
