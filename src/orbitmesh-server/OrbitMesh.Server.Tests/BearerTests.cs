using System.Net;
using System.Text.Json.Nodes;
using OrbitMesh;
using OrbitMesh.Server.Services;
using OrbitMesh.Server.Tests.Harness;

namespace OrbitMesh.Server.Tests;

/// <summary>Calling the API the standard way: <c>Authorization: Bearer &lt;key&gt;</c>, with no EdgeName/PackageName to
/// send - the endpoint says which door it is. The older forms (the AccessKey header, the query string, the names) keep
/// working, and nothing is widened: whatever a Bearer caller gets, it could have got by naming the door itself.</summary>
public sealed class BearerTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static readonly Caller Anonymous = new(null, null);

    private async Task<string> NewTokenAsync(Api api, string name, params string[] scopes)
    {
        var admin = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        using var response = await api.PostAsync("rest/management/credentials/tokens", Caller.Session(admin), new { Name = name, Scopes = scopes });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var token = (string)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["Token"]!;
        await ServerFixture.EventuallyAsync(() => server.Options.CurrentValue.Credentials.Any(c => c.Name == name), "the token should have been reloaded");
        return token;
    }

    // --- A script with a token and nothing else.

    [Fact]
    public async Task A_token_in_a_bearer_header_is_enough_to_call_the_api()
    {
        var api = server.CreateApi();
        var token = await NewTokenAsync(api, "bearer-reader", OrbitMeshScope.EdgesRead);

        using var management = await api.GetAsync("rest/management/edges", Caller.Bearer(token));
        using var consumer = await api.GetAsync(Doors.ConsumerGate, Caller.Bearer(token));
        using var controller = await api.GetAsync(Doors.ControllerGate, Caller.Bearer(token));

        Assert.Equal(HttpStatusCode.OK, management.StatusCode);
        Assert.Equal(HttpStatusCode.OK, consumer.StatusCode);
        Assert.Equal(HttpStatusCode.OK, controller.StatusCode);
    }

    [Fact]
    public async Task The_scopes_still_apply()
    {
        var api = server.CreateApi();
        var token = await NewTokenAsync(api, "bearer-narrow", OrbitMeshScope.EdgesRead);

        using var refused = await api.GetAsync("rest/management/credentials", Caller.Bearer(token));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("credentials:read", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_machine_key_works_the_same_way()
    {
        var api = server.CreateApi();

        using var management = await api.GetAsync(Doors.ManagementGate, Caller.Bearer(Accounts.MachineAll.Key));
        using var consumer = await api.GetAsync(Doors.ConsumerGate, Caller.Bearer(Accounts.MachineNoScope.Key));
        using var noScope = await api.GetAsync(Doors.ManagementGate, Caller.Bearer(Accounts.MachineNoScope.Key));

        Assert.Equal(HttpStatusCode.OK, management.StatusCode);
        Assert.Equal(HttpStatusCode.OK, consumer.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noScope.StatusCode);
    }

    [Fact]
    public async Task The_scheme_name_is_case_insensitive_and_other_schemes_are_not_ours()
    {
        var api = server.CreateApi();

        using var lower = await api.GetAsync(Doors.ConsumerGate, new Caller(null, null, Authorization: "bearer " + Accounts.MachineAll.Key));
        using var basic = await api.GetAsync(Doors.ConsumerGate, new Caller(null, null, Authorization: "Basic " + Convert.ToBase64String("admin:Admin-Passw0rd-1"u8.ToArray())));

        Assert.Equal(HttpStatusCode.OK, lower.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, basic.StatusCode);
    }

    // --- Nothing is widened.

    [Fact]
    public async Task The_edge_and_package_doors_still_need_the_names()
    {
        var api = server.CreateApi();

        using var noNames = await api.GetAsync(Doors.OrbitMeshGate, Caller.Bearer(Accounts.EdgeA.Key));
        using var named = await api.GetAsync(Doors.OrbitMeshGate, Caller.Bearer(Accounts.EdgeA.Key, "edge-a", "Edge"));
        using var wrongEdge = await api.GetAsync(Doors.OrbitMeshGate, Caller.Bearer(Accounts.EdgeB.Key, "edge-a", "Edge"));

        Assert.Equal(HttpStatusCode.Forbidden, noNames.StatusCode);
        Assert.Equal(HttpStatusCode.OK, named.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrongEdge.StatusCode);
    }

    [Fact]
    public async Task Naming_only_one_of_the_two_is_not_filled_in()
    {
        var api = server.CreateApi();

        using var onlyEdge = await api.GetAsync(Doors.ManagementGate, new Caller(Doors.Management, null, Authorization: "Bearer " + Accounts.MachineAll.Key));
        using var onlyPackage = await api.GetAsync(Doors.ManagementGate, new Caller(null, "test", Authorization: "Bearer " + Accounts.MachineAll.Key));

        Assert.Equal(HttpStatusCode.Forbidden, onlyEdge.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, onlyPackage.StatusCode);
    }

    [Fact]
    public async Task A_cookie_session_still_has_to_send_its_identity_headers()
    {
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);

        using var cookieOnly = await api.GetAsync(Doors.ManagementGate, new Caller(null, null, SessionToken: token));

        Assert.Equal(HttpStatusCode.Forbidden, cookieOnly.StatusCode);
    }

    [Fact]
    public async Task A_session_cannot_be_smuggled_in_as_a_bearer_token()
    {
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        var sessionId = server.Sessions.TryGetByToken(token, touch: false)!.Id;

        using var pseudoKey = await api.GetAsync(Doors.ManagementGate, Caller.Bearer(CallerKeys.SessionKeyPrefix + sessionId));
        using var theCookieValue = await api.GetAsync(Doors.ManagementGate, Caller.Bearer(token));

        Assert.Equal(HttpStatusCode.Forbidden, pseudoKey.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, theCookieValue.StatusCode);
    }

    [Fact]
    public async Task The_older_forms_keep_working_and_the_access_key_header_wins_when_both_are_sent()
    {
        var api = server.CreateApi();

        using var legacy = await api.GetAsync(Doors.ManagementGate, Caller.Key(Doors.Management, Accounts.MachineAll.Key));
        using var accessKeyWins = await api.GetAsync(Doors.ConsumerGate, new Caller(null, null, Accounts.MachineAll.Key, Authorization: "Bearer not-a-real-key"));
        using var bearerLoses = await api.GetAsync(Doors.ConsumerGate, new Caller(null, null, "not-a-real-key", Authorization: "Bearer " + Accounts.MachineAll.Key));

        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        Assert.Equal(HttpStatusCode.OK, accessKeyWins.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, bearerLoses.StatusCode);
    }

    // --- Guessing.

    [Fact]
    public async Task A_wrong_bearer_key_counts_as_a_guess()
    {
        var api = server.CreateApi();

        for (var i = 0; i < 10; i++)
        {
            using var refused = await api.GetAsync(Doors.ManagementGate, Caller.Bearer("a-guess-" + i));
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        using var locked = await api.GetAsync(Doors.ConsumerGate, Caller.Bearer(Accounts.MachineAll.Key));
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
    }

    [Fact]
    public async Task Another_scheme_in_the_authorization_header_is_not_a_guess()
    {
        var api = server.CreateApi();

        for (var i = 0; i < 30; i++)
        {
            using var refused = await api.GetAsync(Doors.ManagementGate, new Caller(null, null, Authorization: "Basic dXNlcjpwYXNz"));
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        using var fine = await api.GetAsync(Doors.ConsumerGate, Caller.Bearer(Accounts.MachineAll.Key));
        Assert.Equal(HttpStatusCode.OK, fine.StatusCode);
    }

    // --- The standard hub way: the access_token SignalR sends where a transport can't set headers.

    [Fact]
    public async Task A_hub_accepts_the_bearer_header_and_the_access_token_parameter()
    {
        Assert.True(await HubProbe.ConnectsAsync(server, OrbitMeshHubNames.Consumer, Caller.Bearer(Accounts.MachineAll.Key)));
        Assert.True(await HubProbe.ConnectsAsync(server, OrbitMeshHubNames.Consumer, Anonymous, query: "?access_token=" + Accounts.MachineAll.Key));
        Assert.False(await HubProbe.ConnectsAsync(server, OrbitMeshHubNames.Consumer, Caller.Bearer("not-a-real-key")));
        Assert.False(await HubProbe.ConnectsAsync(server, OrbitMeshHubNames.Consumer, Anonymous, query: "?access_token=not-a-real-key"));
    }

    [Fact]
    public async Task The_access_token_parameter_is_for_hubs_only()
    {
        // A key in a REST URL would end up in logs; only a hub transport that can't send headers needs it there.
        var api = server.CreateApi();

        using var response = await api.GetAsync($"{Doors.ConsumerGate}?access_token={Accounts.MachineAll.Key}", Anonymous);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Package downloads, for a script.

    [Fact]
    public async Task A_token_with_packages_read_can_fetch_a_package_with_a_bearer_header_only()
    {
        var api = server.CreateApi();
        var directory = Path.Combine(server.ContentRoot, "packages");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "bearer.zip"), [1, 2, 3]);
        var reader = await NewTokenAsync(api, "bearer-package-reader", OrbitMeshScope.PackagesRead);
        var other = await NewTokenAsync(api, "bearer-no-package-read", OrbitMeshScope.EdgesRead);

        using var allowed = await api.GetAsync("packages/bearer.zip", Caller.Bearer(reader));
        using var refused = await api.GetAsync("packages/bearer.zip", Caller.Bearer(other));
        using var edgeWithoutNames = await api.GetAsync("packages/bearer.zip", Caller.Bearer(Accounts.EdgeA.Key));

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, edgeWithoutNames.StatusCode);
    }
}
