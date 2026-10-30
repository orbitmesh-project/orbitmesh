using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using OrbitMesh.Server.Services;
using OrbitMesh.Server.Tests.Harness;

namespace OrbitMesh.Server.Tests;

/// <summary>What a person gets by signing in: an opaque cookie that only the Server can read, and that the Server
/// can end - on sign-out, on idle time, when the account changes.</summary>
public sealed class SessionTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    [Fact]
    public async Task Signing_in_sets_a_cookie_the_page_cannot_read_and_returns_the_session()
    {
        var api = server.CreateApi();

        using var response = await api.LoginAsync(Accounts.Admin.Name, Accounts.Admin.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Api.SessionCookieHeaderOf(response);
        Assert.NotNull(cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        // Plain HTTP in the test host: Secure is only set when the request itself was HTTPS.
        Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal(Accounts.Admin.Name, (string?)body["Name"]);
        Assert.False((bool)body["IsFederated"]!);
    }

    [Fact]
    public async Task The_session_carries_the_scopes_of_the_account_and_nothing_a_person_cannot_use()
    {
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);

        using var response = await api.GetAsync("rest/auth/me", Caller.Session(token));

        var scopes = ((JsonArray)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["Scopes"]!).Select(s => (string)s!).ToList();
        Assert.Equal(OrbitMeshScope.ForPeople.Order(), scopes.Order());
        Assert.DoesNotContain(OrbitMeshScope.EdgeConnect, scopes);
        Assert.DoesNotContain(OrbitMeshScope.PackageConnect, scopes);
    }

    [Fact]
    public async Task The_server_keeps_only_a_hash_of_the_token()
    {
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Viewer.Name, Accounts.Viewer.Password);
        var stored = await File.ReadAllTextAsync(Path.Combine(server.ContentRoot, "keys", "sessions.json"));

        Assert.DoesNotContain(token, stored, StringComparison.Ordinal);
        Assert.Contains(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), stored, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Without_a_valid_cookie_there_is_no_session()
    {
        var api = server.CreateApi();

        using var none = await api.GetAsync("rest/auth/me", Caller.Anonymous(Doors.Management));
        using var bogus = await api.GetAsync("rest/auth/me", Caller.Session("not-a-real-token"));

        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, bogus.StatusCode);
    }

    [Fact]
    public async Task Wrong_passwords_unknown_names_and_disabled_accounts_are_refused_alike()
    {
        var api = server.CreateApi();

        var refusals = new List<(HttpStatusCode Status, string Body)>();
        foreach (var (name, password) in new[]
        {
            (Accounts.Admin.Name, "wrong-password"),
            ("nobody-by-that-name", "whatever-password"),
            (Accounts.Disabled.Name, Accounts.Disabled.Password)
        })
        {
            using var response = await api.LoginAsync(name, password);
            refusals.Add((response.StatusCode, await response.Content.ReadAsStringAsync()));
        }

        Assert.All(refusals, r => Assert.Equal(HttpStatusCode.Forbidden, r.Status));
        // The answer doesn't tell which of the three it was.
        Assert.Single(refusals.Select(r => r.Body).Distinct());
    }

    [Fact]
    public async Task A_machine_key_never_signs_anybody_in()
    {
        var api = server.CreateApi();

        using var response = await api.LoginAsync(Accounts.MachineAll.Name, Accounts.MachineAll.Key);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(Api.SessionCookieHeaderOf(response));
    }

    [Fact]
    public async Task An_account_with_no_scopes_cannot_sign_in_and_is_told_why()
    {
        var api = server.CreateApi();

        using var response = await api.LoginAsync(Accounts.NoScopes.Name, Accounts.NoScopes.Password);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("no scopes", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Null(Api.SessionCookieHeaderOf(response));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Name\":\"admin\"}")]
    [InlineData("{\"Name\":\"\",\"Password\":\"x\"}")]
    public async Task An_incomplete_sign_in_request_is_a_bad_request(string body)
    {
        var api = server.CreateApi();

        using var response = await api.LoginRawAsync(body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_sign_in_posted_from_another_origin_is_refused_even_with_the_right_password()
    {
        var api = server.CreateApi();

        using var response = await api.LoginAsync(Accounts.Admin.Name, Accounts.Admin.Password, origin: "https://evil.example");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(Api.SessionCookieHeaderOf(response));
    }

    [Fact]
    public async Task Signing_out_ends_that_session_only()
    {
        var api = server.CreateApi();
        var first = await api.SignInAsync(Accounts.Viewer.Name, Accounts.Viewer.Password);
        var second = await api.SignInAsync(Accounts.Viewer.Name, Accounts.Viewer.Password);

        using var logout = await api.PostAsync("rest/auth/logout", Caller.Session(first));

        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        using var firstAfter = await api.GetAsync("rest/auth/me", Caller.Session(first));
        using var secondAfter = await api.GetAsync("rest/auth/me", Caller.Session(second));
        Assert.Equal(HttpStatusCode.Unauthorized, firstAfter.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondAfter.StatusCode);
        using var management = await api.GetAsync(Doors.ManagementGate, Caller.Session(first));
        Assert.Equal(HttpStatusCode.Forbidden, management.StatusCode);
    }

    [Fact]
    public async Task A_session_left_idle_too_long_expires()
    {
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Viewer.Name, Accounts.Viewer.Password);
        var idleMinutes = server.Options.CurrentValue.Auth.SessionIdleMinutes;
        server.Sessions.TryGetByToken(token, touch: false)!.LastSeenUtc = DateTime.UtcNow.AddMinutes(-(idleMinutes + 1));

        using var me = await api.GetAsync("rest/auth/me", Caller.Session(token));
        using var management = await api.GetAsync(Doors.ManagementGate, Caller.Session(token));

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, management.StatusCode);
    }

    [Fact]
    public async Task Using_a_session_keeps_it_from_idling_out()
    {
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Viewer.Name, Accounts.Viewer.Password);
        var idleMinutes = server.Options.CurrentValue.Auth.SessionIdleMinutes;
        var session = server.Sessions.TryGetByToken(token, touch: false)!;
        session.LastSeenUtc = DateTime.UtcNow.AddMinutes(-(idleMinutes - 1));

        using var first = await api.GetAsync("rest/auth/me", Caller.Session(token));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True(DateTime.UtcNow - session.LastSeenUtc < TimeSpan.FromMinutes(1), "A request should have renewed the idle window.");
    }

    // --- The cookie is sent by the browser on its own, so a request riding on it has to prove it came from the Console.

    [Fact]
    public async Task A_state_changing_request_on_a_cookie_must_carry_the_identity_headers()
    {
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        var noHeaders = new Caller(EdgeName: null, PackageName: null, SessionToken: token);

        using var forged = await api.PostAsync("rest/management/configuration/recoveryoptions", noHeaders, new { });
        using var genuine = await api.PostAsync("rest/management/configuration/recoveryoptions", Caller.Session(token), new { });

        Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);
        Assert.Equal("Forbidden", await forged.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, genuine.StatusCode);
    }

    [Fact]
    public async Task A_state_changing_request_on_a_cookie_cannot_take_its_identity_from_the_url()
    {
        // The attack this rule exists for: a form on another site can post to this server (the browser attaches the
        // cookie) and can write the URL, but it cannot set a header. So the identity must be in a header.
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        var cookieOnly = new Caller(EdgeName: null, PackageName: null, SessionToken: token);
        const string identityInTheUrl = "?EdgeName=Management&PackageName=forged-form";

        using var forged = await api.PostAsync("rest/management/configuration/recoveryoptions" + identityInTheUrl, cookieOnly, new { });
        using var read = await api.GetAsync("rest/management/CheckAccess" + identityInTheUrl, cookieOnly);

        Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);
        Assert.Equal("Forbidden", await forged.Content.ReadAsStringAsync());
        // Reading is not state-changing, and the cookie's origin check still applies to it.
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task A_cookie_is_refused_on_a_request_from_another_origin()
    {
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Viewer.Name, Accounts.Viewer.Password);

        using var foreign = await api.GetAsync(Doors.ManagementGate, Caller.Session(token) with { Origin = "https://evil.example" });
        using var own = await api.GetAsync(Doors.ManagementGate, Caller.Session(token) with { Origin = "http://localhost" });

        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }

    [Fact]
    public async Task A_session_cannot_be_presented_as_an_access_key()
    {
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        var sessionId = server.Sessions.TryGetByToken(token, touch: false)!.Id;

        using var pseudoKey = await api.GetAsync(Doors.ManagementGate, Caller.Key(Doors.Management, CallerKeys.SessionKeyPrefix + sessionId));
        using var storedHash = await api.GetAsync(Doors.ManagementGate, Caller.Key(Doors.Management, sessionId));

        Assert.Equal(HttpStatusCode.Forbidden, pseudoKey.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, storedHash.StatusCode);
    }

    [Fact]
    public async Task The_session_pseudo_key_is_never_readable_back_from_the_cookie_value()
    {
        // The cookie holds a random token; the pseudo key is derived from its hash. Knowing the hash (which is
        // what sessions.json holds) must not be enough to build a working cookie.
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        var sessionId = server.Sessions.TryGetByToken(token, touch: false)!.Id;

        using var withHashAsToken = await api.GetAsync("rest/auth/me", Caller.Session(sessionId));

        Assert.Equal(HttpStatusCode.Unauthorized, withHashAsToken.StatusCode);
    }
}
