using System.Net;
using System.Text.Json.Nodes;
using OrbitMesh.Server.Services;
using OrbitMesh.Server.Tests.Harness;

namespace OrbitMesh.Server.Tests;

/// <summary>A person's session (password or single sign-on) opens the Console's doors - Management, Controller,
/// Consumer - and nothing that belongs to a machine: an Edge's own door, the developer edge. Its scopes are
/// the account's own, or - for single sign-on - those the role mappings give the groups it signed in with.</summary>
public sealed class PersonDoorsTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    public static IEnumerable<object[]> Doors_() =>
    [
        ["management", Doors.Management, Doors.ManagementGate, HttpStatusCode.OK],
        ["controller", Doors.Controller, Doors.ControllerGate, HttpStatusCode.OK],
        ["consumer", Doors.Consumer, Doors.ConsumerGate, HttpStatusCode.OK],
        ["an edge's own door", "edge-a", Doors.OrbitMeshGate, HttpStatusCode.Forbidden],
        ["the developer edge", Doors.Developer, Doors.OrbitMeshGate, HttpStatusCode.Forbidden]
    ];

    [Theory]
    [MemberData(nameof(Doors_))]
    public async Task A_password_session_opens_the_console_doors_only(string door, string edge, string path, HttpStatusCode expected)
    {
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);

        using var response = await api.GetAsync(path, Caller.Session(token, edge));

        Assert.True(response.StatusCode == expected, $"{door}: expected {(int)expected}, got {(int)response.StatusCode}");
    }

    [Theory]
    [MemberData(nameof(Doors_))]
    public async Task A_single_sign_on_session_opens_the_console_doors_only(string door, string edge, string path, HttpStatusCode expected)
    {
        var api = server.CreateApi();
        var token = server.CreateFederatedSession("alice", Accounts.AdminGroup);

        using var response = await api.GetAsync(path, Caller.Session(token, edge));

        Assert.True(response.StatusCode == expected, $"{door}: expected {(int)expected}, got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task A_read_only_person_can_look_but_not_change()
    {
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Viewer.Name, Accounts.Viewer.Password);

        using var list = await api.GetAsync("rest/management/credentials", Caller.Session(token));
        using var create = await api.PostAsync("rest/management/credentials", Caller.Session(token), new
        {
            Name = "made-by-a-viewer",
            AccessKey = "Whatever-Passw0rd-1",
            Kind = "Human",
            Enable = true,
            Scopes = new[] { "edges:read" }
        });

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.DoesNotContain(server.Options.CurrentValue.Credentials, c => c.Name == "made-by-a-viewer");
    }

    // --- Single sign-on: scopes come from the groups, through the role mappings.

    [Fact]
    public async Task An_administrator_group_gets_every_scope_a_person_can_hold()
    {
        var api = server.CreateApi();
        var token = server.CreateFederatedSession("alice", Accounts.AdminGroup);

        var me = await ReadMeAsync(api, token);

        Assert.Equal("oidc:alice", (string?)me["Name"]);
        Assert.True((bool)me["IsFederated"]!);
        Assert.Equal(OrbitMeshScope.ForPeople.Order(), ScopesOf(me).Order());
    }

    [Fact]
    public async Task A_viewer_group_gets_the_read_scopes()
    {
        var api = server.CreateApi();
        var token = server.CreateFederatedSession("bob", Accounts.ViewerGroup);

        var me = await ReadMeAsync(api, token);

        Assert.Equal(Accounts.ReadScopes.Order(), ScopesOf(me).Order());
        using var write = await api.PostAsync("rest/management/configuration", Caller.Session(token), new { });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task Several_groups_give_the_union_of_their_scopes()
    {
        var api = server.CreateApi();
        var token = server.CreateFederatedSession("carol", Accounts.TempGroup, Accounts.ViewerGroup);

        var me = await ReadMeAsync(api, token);

        Assert.Equal(Accounts.ReadScopes.Union([OrbitMeshScope.EdgesRead]).Order(), ScopesOf(me).Order());
    }

    [Fact]
    public async Task A_group_that_is_not_mapped_gets_nothing_at_all()
    {
        var api = server.CreateApi();
        var token = server.CreateFederatedSession("mallory", Accounts.UnmappedGroup);

        using var me = await api.GetAsync("rest/auth/me", Caller.Session(token));
        using var management = await api.GetAsync(Doors.ManagementGate, Caller.Session(token));
        using var consumer = await api.GetAsync(Doors.ConsumerGate, Caller.Session(token, Doors.Consumer));

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, management.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, consumer.StatusCode);
    }

    [Fact]
    public async Task Group_names_are_matched_without_regard_to_case()
    {
        var api = server.CreateApi();
        var token = server.CreateFederatedSession("dave", Accounts.AdminGroup.ToUpperInvariant());

        using var response = await api.GetAsync(Doors.ManagementGate, Caller.Session(token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Editing_a_role_mapping_applies_to_sessions_that_are_already_open()
    {
        var api = server.CreateApi();
        var token = server.CreateFederatedSession("erin", Accounts.TempGroup);
        using (var before = await api.GetAsync("rest/management/credentials", Caller.Session(token)))
        {
            Assert.Equal(HttpStatusCode.Forbidden, before.StatusCode);
        }

        // An administrator hand-edits appsettings.json, as they would.
        var configuration = server.ReadConfigurationFile();
        var mapping = configuration["OrbitMesh"]!["auth"]!["oidc"]!["roleMappings"]!.AsArray()
            .Single(m => (string?)m!["group"] == Accounts.TempGroup)!;
        mapping["scopes"] = new JsonArray(OrbitMeshScope.EdgesRead, OrbitMeshScope.CredentialsRead);
        await File.WriteAllTextAsync(Path.Combine(server.ContentRoot, "appsettings.json"), configuration.ToJsonString());

        await ServerFixture.EventuallyAsync(
            () => server.Options.CurrentValue.Auth.Oidc.RoleMappings.Any(m => m.Group == Accounts.TempGroup && m.Scopes.Contains(OrbitMeshScope.CredentialsRead)),
            "the edited mapping should have been reloaded");
        using var after = await api.GetAsync("rest/management/credentials", Caller.Session(token));
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task A_local_credential_cannot_take_a_single_sign_on_name()
    {
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);

        using var response = await api.PostAsync("rest/management/credentials", Caller.Session(token), new
        {
            Name = "oidc:alice",
            AccessKey = "Whatever-Passw0rd-1",
            Kind = "Human",
            Enable = true,
            Scopes = new[] { "edges:read" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(server.Options.CurrentValue.Credentials, c => c.Name.StartsWith("oidc:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_single_sign_on_name_cannot_be_used_to_sign_in_with_a_password()
    {
        var api = server.CreateApi();
        server.CreateFederatedSession("alice", Accounts.AdminGroup);

        using var response = await api.LoginAsync("oidc:alice", "any-password");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<JsonNode> ReadMeAsync(Api api, string token)
    {
        using var response = await api.GetAsync("rest/auth/me", Caller.Session(token));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    private static List<string> ScopesOf(JsonNode me) => ((JsonArray)me["Scopes"]!).Select(s => (string)s!).ToList();
}
