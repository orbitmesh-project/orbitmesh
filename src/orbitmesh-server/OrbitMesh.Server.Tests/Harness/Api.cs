using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OrbitMesh.Server.Tests.Harness;

/// <summary>Who is calling, expressed the way the wire carries it: the EdgeName/PackageName/AccessKey headers,
/// and/or the Console's session cookie.</summary>
public sealed record Caller(string? EdgeName = null, string? PackageName = "test", string? AccessKey = null, string? SessionToken = null, string? Origin = null, string? Authorization = null)
{
    /// <summary>A script calling the API the standard way: <c>Authorization: Bearer &lt;key&gt;</c>, and no names at all unless given.</summary>
    public static Caller Bearer(string key, string? edge = null, string? package = null) => new(edge, package, Authorization: "Bearer " + key);

    public static Caller Anonymous(string edge) => new(edge);

    public static Caller Key(string edge, string accessKey, string package = "test") => new(edge, package, accessKey);

    public static Caller Session(string token, string edge = "Management") => new(edge, "test", null, token);
}

public static class Doors
{
    public const string Management = "Management";
    public const string Consumer = "Consumer";
    public const string Controller = "Controller";
    public const string Developer = "Developer";

    // A request needs only to get through the gate; the endpoint behind it does nothing.
    public const string ManagementGate = "rest/management/CheckAccess";
    public const string ConsumerGate = "rest/consumer/CheckAccess";
    public const string ControllerGate = "rest/controller/CheckAccess";
    public const string OrbitMeshGate = "rest/orbitmesh/CheckAccess";
}

public sealed class Api(ServerFixture server, string ip)
{
    public const string SessionCookie = "orbitmesh_session";

    public string Ip { get; } = ip;

    public HttpClient Http { get; } = server.CreateHttpClient(ip);

    public Task<HttpResponseMessage> GetAsync(string path, Caller caller) => SendAsync(HttpMethod.Get, path, caller);

    public Task<HttpResponseMessage> PostAsync(string path, Caller caller, object? json = null) => SendAsync(HttpMethod.Post, path, caller, json ?? new { });

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, Caller caller, object? json = null)
    {
        var request = new HttpRequestMessage(method, "/" + path.TrimStart('/'));
        if (json != null)
        {
            request.Content = new StringContent(json is string text ? text : JsonSerializer.Serialize(json), Encoding.UTF8, "application/json");
        }
        if (caller.EdgeName != null)
        {
            request.Headers.TryAddWithoutValidation("EdgeName", caller.EdgeName);
        }
        if (caller.PackageName != null)
        {
            request.Headers.TryAddWithoutValidation("PackageName", caller.PackageName);
        }
        if (caller.AccessKey != null)
        {
            request.Headers.TryAddWithoutValidation("AccessKey", caller.AccessKey);
        }
        if (caller.Authorization != null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", caller.Authorization);
        }
        if (caller.SessionToken != null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", $"{SessionCookie}={caller.SessionToken}");
        }
        if (caller.Origin != null)
        {
            request.Headers.TryAddWithoutValidation("Origin", caller.Origin);
        }
        return await Http.SendAsync(request);
    }

    public Task<HttpResponseMessage> LoginAsync(string name, string password, string? origin = null) =>
        LoginRawAsync(JsonSerializer.Serialize(new { Name = name, Password = password }), origin);

    public async Task<HttpResponseMessage> LoginRawAsync(string body, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/rest/auth/login")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (origin != null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }
        return await Http.SendAsync(request);
    }

    /// <summary>Signs in and returns the session token the Server put in the cookie; fails the test if it didn't.</summary>
    public async Task<string> SignInAsync(string name, string password)
    {
        using var response = await LoginAsync(name, password);
        Assert.True(response.IsSuccessStatusCode, $"Signing in as '{name}' was refused: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return SessionTokenOf(response) ?? throw new Xunit.Sdk.XunitException("The sign-in response set no session cookie.");
    }

    public static string? SessionTokenOf(HttpResponseMessage response)
    {
        var cookie = SessionCookieHeaderOf(response);
        if (cookie == null)
        {
            return null;
        }
        var value = cookie[(SessionCookie.Length + 1)..].Split(';')[0];
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public static string? SessionCookieHeaderOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(SessionCookie + "=", StringComparison.Ordinal))
            : null;
}
