using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using OrbitMesh;
using OrbitMesh.Server.Tests.Harness;

namespace OrbitMesh.Server.Tests;

/// <summary>Taking a new Edge in: it asks to be approved (<c>rest/enroll</c>), an administrator approves it in the
/// Console, and it connects with the key the Server minted. The administrator does not hold <c>edge:connect</c> - no
/// person does, it is what an Edge's own credential carries - so this must work for someone who may manage edges.</summary>
public sealed class EdgeApprovalTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private async Task<string> EnrollAsync(Api api, string edgeName)
    {
        var instanceId = Guid.NewGuid().ToString();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/rest/enroll")
        {
            Content = JsonContent.Create(new { EdgeName = edgeName, InstanceId = instanceId, MachineName = "test-machine", OSVersion = "1", OSCaption = "test", Platform = "Unix" })
        };
        request.Headers.Add(OrbitMeshHeaderNames.EnrollSecret, new string('s', 40));
        using var response = await api.Http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return instanceId;
    }

    private static string NewName(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task An_administrator_approves_an_edge_and_the_edge_connects_with_the_key_it_is_given()
    {
        var api = server.CreateApi();
        var admin = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        var name = NewName("approved");
        var instanceId = await EnrollAsync(api, name);

        using var approve = await api.PostAsync($"rest/management/edges/pending/{instanceId}/approve", Caller.Session(admin), new { Name = name });

        Assert.True(approve.IsSuccessStatusCode, $"approving answered {(int)approve.StatusCode}: {await approve.Content.ReadAsStringAsync()}");
        var key = (string)JsonNode.Parse(await approve.Content.ReadAsStringAsync())!["accessKey"]!;
        await ServerFixture.EventuallyAsync(() => server.Options.CurrentValue.Edges.Any(e => e.Name == name), "the new edge should have been reloaded");
        using var door = await api.GetAsync(Doors.OrbitMeshGate, Caller.Key(name, key, "Edge"));
        Assert.Equal(HttpStatusCode.OK, door.StatusCode);
        Assert.True(await HubProbe.ConnectsAsync(server, OrbitMeshHubNames.Edge, Caller.Key(name, key, "Edge")));
    }

    [Fact]
    public async Task Approving_needs_the_right_to_manage_edges()
    {
        // credentials:manage is not enough on its own - the Manager account holds it, and not edges:write.
        var api = server.CreateApi();
        var manager = await api.SignInAsync(Accounts.Manager.Name, Accounts.Manager.Password);
        var name = NewName("refused");
        var instanceId = await EnrollAsync(api, name);

        using var approve = await api.PostAsync($"rest/management/edges/pending/{instanceId}/approve", Caller.Session(manager), new { Name = name });

        Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);
        Assert.DoesNotContain(server.Options.CurrentValue.Edges, e => e.Name == name);
    }

    [Fact]
    public async Task An_administrator_can_manage_the_credential_of_an_edge()
    {
        var api = server.CreateApi();
        var admin = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        var name = NewName("managed");
        var instanceId = await EnrollAsync(api, name);
        using var approve = await api.PostAsync($"rest/management/edges/pending/{instanceId}/approve", Caller.Session(admin), new { Name = name });
        Assert.True(approve.IsSuccessStatusCode, await approve.Content.ReadAsStringAsync());
        await ServerFixture.EventuallyAsync(() => server.Options.CurrentValue.Credentials.Any(c => c.Name == name), "the credential should have been reloaded");
        var storedKeyBefore = server.Options.CurrentValue.Credentials.Single(c => c.Name == name).AccessKey;

        // Disable it, give it a new key, enable it again: all through the ordinary credential save.
        using var disable = await api.PostAsync("rest/management/credentials", Caller.Session(admin),
            new { Name = name, AccessKey = "", Kind = "Machine", Enable = false, Scopes = new[] { OrbitMeshScope.EdgeConnect } });
        using var rekey = await api.PostAsync("rest/management/credentials", Caller.Session(admin),
            new { Name = name, AccessKey = "a-new-key-for-the-edge-1234", Kind = "Machine", Enable = true, Scopes = new[] { OrbitMeshScope.EdgeConnect } });

        Assert.True(disable.IsSuccessStatusCode, $"disabling answered {(int)disable.StatusCode}: {await disable.Content.ReadAsStringAsync()}");
        Assert.True(rekey.IsSuccessStatusCode, $"re-keying answered {(int)rekey.StatusCode}: {await rekey.Content.ReadAsStringAsync()}");
        await ServerFixture.EventuallyAsync(
            () => server.Options.CurrentValue.Credentials.Single(c => c.Name == name) is { Enable: true } now && now.AccessKey != storedKeyBefore,
            "the re-keyed credential should have been reloaded");
        using var door = await api.GetAsync(Doors.OrbitMeshGate, Caller.Key(name, "a-new-key-for-the-edge-1234", "Edge"));
        Assert.Equal(HttpStatusCode.OK, door.StatusCode);
    }

    [Fact]
    public async Task Someone_who_cannot_manage_edges_still_cannot_hand_out_edge_connect()
    {
        // The Manager holds credentials:manage but not edges:write: it may not create a credential that can connect as an Edge.
        var api = server.CreateApi();
        var manager = await api.SignInAsync(Accounts.Manager.Name, Accounts.Manager.Password);

        using var response = await api.PostAsync("rest/management/credentials", Caller.Session(manager),
            new { Name = NewName("sneaky-edge"), AccessKey = "a-key-for-a-sneaky-edge-1234", Kind = "Machine", Enable = true, Scopes = new[] { OrbitMeshScope.EdgeConnect } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
