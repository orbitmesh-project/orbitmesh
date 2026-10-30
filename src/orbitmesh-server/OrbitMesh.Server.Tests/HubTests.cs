using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using OrbitMesh;
using OrbitMesh.Server.Tests.Harness;

namespace OrbitMesh.Server.Tests;

/// <summary>SignalR hubs decide in OnConnectedAsync, with the same rules as the REST doors: a rejected connection
/// is aborted by the Server right after the handshake. "Accepted" means the connection is still open a moment
/// later; "rejected" means the Server closed it.</summary>
public sealed class HubTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static readonly TimeSpan AcceptedWindow = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan RejectedDeadline = TimeSpan.FromSeconds(5);

    // --- Consumer hub: any enabled credential, or any person's session that holds a scope.

    [Fact]
    public async Task The_consumer_hub_accepts_any_enabled_credential_scopes_or_not()
    {
        Assert.True(await ConnectsAsync(OrbitMeshHubNames.Consumer, Caller.Key(Doors.Consumer, Accounts.MachineAll.Key)));
        Assert.True(await ConnectsAsync(OrbitMeshHubNames.Consumer, Caller.Key(Doors.Consumer, Accounts.MachineNoScope.Key)));
    }

    [Fact]
    public async Task The_consumer_hub_refuses_missing_wrong_and_disabled_keys()
    {
        Assert.False(await ConnectsAsync(OrbitMeshHubNames.Consumer, Caller.Anonymous(Doors.Consumer)));
        Assert.False(await ConnectsAsync(OrbitMeshHubNames.Consumer, Caller.Key(Doors.Consumer, "not-a-real-key")));
        Assert.False(await ConnectsAsync(OrbitMeshHubNames.Consumer, Caller.Key(Doors.Consumer, Accounts.MachineDisabled.Key)));
    }

    [Fact]
    public async Task The_consumer_hub_accepts_a_signed_in_person_and_refuses_a_stale_cookie()
    {
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Viewer.Name, Accounts.Viewer.Password);

        Assert.True(await ConnectsAsync(OrbitMeshHubNames.Consumer, Caller.Session(token, Doors.Consumer)));
        Assert.False(await ConnectsAsync(OrbitMeshHubNames.Consumer, Caller.Session("an-expired-or-forged-token", Doors.Consumer)));
    }

    [Fact]
    public async Task The_consumer_hub_follows_single_sign_on_groups()
    {
        Assert.True(await ConnectsAsync(OrbitMeshHubNames.Consumer, Caller.Session(server.CreateFederatedSession("alice", Accounts.AdminGroup), Doors.Consumer)));
        Assert.False(await ConnectsAsync(OrbitMeshHubNames.Consumer, Caller.Session(server.CreateFederatedSession("mallory", Accounts.UnmappedGroup), Doors.Consumer)));
    }

    // --- Control hub (the Console's own): a credential or person holding at least one scope.

    [Fact]
    public async Task The_control_hub_needs_at_least_one_scope()
    {
        var api = server.CreateApi();
        var admin = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);

        Assert.True(await ConnectsAsync(OrbitMeshHubNames.Control, Caller.Key(Doors.Controller, Accounts.MachineAll.Key)));
        Assert.True(await ConnectsAsync(OrbitMeshHubNames.Control, Caller.Session(admin, Doors.Controller)));
        Assert.False(await ConnectsAsync(OrbitMeshHubNames.Control, Caller.Key(Doors.Controller, Accounts.MachineNoScope.Key)));
        Assert.False(await ConnectsAsync(OrbitMeshHubNames.Control, Caller.Anonymous(Doors.Controller)));
    }

    // --- Edge hub: only the credential assigned to that edge. A person's session is never good for it.

    [Fact]
    public async Task The_edge_hub_accepts_the_credential_assigned_to_the_edge()
    {
        Assert.True(await ConnectsAsync(OrbitMeshHubNames.Edge, Caller.Key("edge-a", Accounts.EdgeA.Key, "Edge")));
    }

    [Fact]
    public async Task The_edge_hub_refuses_every_other_credential_and_every_session()
    {
        var api = server.CreateApi();
        var admin = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);

        Assert.False(await ConnectsAsync(OrbitMeshHubNames.Edge, Caller.Key("edge-a", Accounts.EdgeB.Key, "Edge")));
        Assert.False(await ConnectsAsync(OrbitMeshHubNames.Edge, Caller.Key("edge-a", Accounts.MachineAll.Key, "Edge")));
        Assert.False(await ConnectsAsync(OrbitMeshHubNames.Edge, Caller.Session(admin, "edge-a") with { PackageName = "Edge" }));
        Assert.False(await ConnectsAsync(OrbitMeshHubNames.Edge, Caller.Session(server.CreateFederatedSession("alice", Accounts.AdminGroup), "edge-a") with { PackageName = "Edge" }));
        Assert.False(await ConnectsAsync(OrbitMeshHubNames.Edge, Caller.Anonymous("edge-a") with { PackageName = "Edge" }));
    }

    [Fact]
    public async Task The_package_hub_refuses_a_session_and_an_unassigned_credential()
    {
        var api = server.CreateApi();
        var admin = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);

        Assert.False(await ConnectsAsync(OrbitMeshHubNames.OrbitMesh, Caller.Session(admin, "edge-a") with { PackageName = "some-package" }));
        Assert.False(await ConnectsAsync(OrbitMeshHubNames.OrbitMesh, Caller.Key("edge-a", Accounts.MachineAll.Key, "some-package")));
    }

    // --- Hub failures count against the same lockout as REST ones.

    [Fact]
    public async Task Failed_hub_connections_lock_the_address_out_of_the_rest_api_too()
    {
        var ip = server.CreateApi().Ip;

        for (var i = 0; i < 10; i++)
        {
            await ConnectsAsync(OrbitMeshHubNames.Consumer, Caller.Key(Doors.Consumer, "a-guess"), ip);
        }

        var api = new Api(server, ip);
        using var response = await api.GetAsync(Doors.ConsumerGate, Caller.Key(Doors.Consumer, Accounts.MachineAll.Key));
        Assert.Equal(System.Net.HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    private async Task<bool> ConnectsAsync(string hub, Caller caller, string? ip = null)
    {
        ip ??= server.CreateApi().Ip;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.Server.BaseAddress, $"signalr/hubs/{hub}"), options =>
            {
                options.HttpMessageHandlerFactory = _ => server.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.Headers["X-Test-Remote-Ip"] = ip;
                if (caller.EdgeName != null) { options.Headers["EdgeName"] = caller.EdgeName; }
                if (caller.PackageName != null) { options.Headers["PackageName"] = caller.PackageName; }
                if (caller.AccessKey != null) { options.Headers["AccessKey"] = caller.AccessKey; }
                if (caller.SessionToken != null) { options.Headers["Cookie"] = $"{Api.SessionCookie}={caller.SessionToken}"; }
            })
            .Build();
        connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        try
        {
            await connection.StartAsync();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Not a refusal: the hub route itself is wrong, and every "rejected" assertion would pass for nothing.
            throw new InvalidOperationException($"The hub route for {hub} does not exist.", ex);
        }
        catch (Exception)
        {
            return false;
        }

        // Rejected connections are closed by the Server; accepted ones stay up. Wait long enough to tell.
        var finished = await Task.WhenAny(closed.Task, Task.Delay(AcceptedWindow)) == closed.Task;
        if (!finished)
        {
            return true;
        }
        return false;
    }
}
