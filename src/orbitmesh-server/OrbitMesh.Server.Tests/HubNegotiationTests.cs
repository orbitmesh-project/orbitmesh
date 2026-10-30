using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OrbitMesh;
using OrbitMesh.Server.Services;
using OrbitMesh.Server.Tests.Harness;

namespace OrbitMesh.Server.Tests;

/// <summary>A hub is authorized when its connection is negotiated, by the same rules and the same pipeline as the REST
/// API: a refused caller gets an HTTP 403 (429 when locked out) and never a connection to abort. These look at the
/// negotiation itself; <see cref="HubTests"/> looks at who ends up connected.</summary>
public sealed class HubNegotiationTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static readonly Caller Anonymous = new(null, null);

    private static string Negotiate(string hub, string query = "") => $"signalr/hubs/{hub}/negotiate?negotiateVersion=1{query}";

    [Fact]
    public void No_hub_is_public()
    {
        var hubs = server.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => (e.RoutePattern.RawText ?? "").TrimStart('/').StartsWith("signalr/hubs/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(hubs);
        Assert.All(hubs, hub =>
        {
            Assert.Null(hub.Metadata.GetMetadata<IAllowAnonymous>());
            Assert.NotEmpty(hub.Metadata.GetOrderedMetadata<IAuthorizeData>());
        });
    }

    [Fact]
    public async Task A_caller_with_no_credential_is_refused_at_the_negotiation_of_every_hub()
    {
        var api = server.CreateApi();

        foreach (var hub in new[] { OrbitMeshHubNames.Consumer, OrbitMeshHubNames.Control, OrbitMeshHubNames.Edge, OrbitMeshHubNames.OrbitMesh })
        {
            using var response = await api.PostAsync(Negotiate(hub), Anonymous);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{hub} answered {(int)response.StatusCode} to an anonymous negotiation");
        }
    }

    [Fact]
    public async Task A_caller_who_may_connect_is_given_a_connection_to_open()
    {
        var api = server.CreateApi();

        using var response = await api.PostAsync(Negotiate(OrbitMeshHubNames.Consumer), Caller.Key(Doors.Consumer, Accounts.MachineAll.Key));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrEmpty((string?)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["connectionToken"]));
    }

    [Fact]
    public async Task Each_hub_applies_its_own_door()
    {
        var api = server.CreateApi();
        var admin = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);

        async Task<HttpStatusCode> AskAsync(string hub, Caller caller)
        {
            using var response = await api.PostAsync(Negotiate(hub), caller);
            return response.StatusCode;
        }

        // The Consumer hub: any enabled credential - and it is judged by its own name, whatever the caller says.
        Assert.Equal(HttpStatusCode.OK, await AskAsync(OrbitMeshHubNames.Consumer, Caller.Key("whatever", Accounts.MachineNoScope.Key)));
        // The Control hub: at least one scope.
        Assert.Equal(HttpStatusCode.OK, await AskAsync(OrbitMeshHubNames.Control, Caller.Key(Doors.Controller, Accounts.MachineRead.Key)));
        Assert.Equal(HttpStatusCode.Forbidden, await AskAsync(OrbitMeshHubNames.Control, Caller.Key(Doors.Controller, Accounts.MachineNoScope.Key)));
        // The Edge hub: the credential assigned to that Edge - never a session, never someone else's key.
        Assert.Equal(HttpStatusCode.OK, await AskAsync(OrbitMeshHubNames.Edge, Caller.Key("edge-a", Accounts.EdgeA.Key, "Edge")));
        Assert.Equal(HttpStatusCode.Forbidden, await AskAsync(OrbitMeshHubNames.Edge, Caller.Key("edge-a", Accounts.EdgeB.Key, "Edge")));
        Assert.Equal(HttpStatusCode.Forbidden, await AskAsync(OrbitMeshHubNames.Edge, Caller.Session(admin, "edge-a") with { PackageName = "Edge" }));
        // The package hub: a package's (or its Edge's) credential, with package:connect.
        Assert.Equal(HttpStatusCode.Forbidden, await AskAsync(OrbitMeshHubNames.OrbitMesh, Caller.Key("edge-a", Accounts.MachineAll.Key, "some-package")));
    }

    [Fact]
    public async Task Guessing_at_a_hub_locks_the_address_out_at_the_negotiation()
    {
        var api = server.CreateApi();

        for (var i = 0; i < 10; i++)
        {
            using var refused = await api.PostAsync(Negotiate(OrbitMeshHubNames.Consumer), Caller.Key(Doors.Consumer, "a-guess-" + i));
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        using var locked = await api.PostAsync(Negotiate(OrbitMeshHubNames.Consumer), Caller.Key(Doors.Consumer, Accounts.MachineAll.Key));
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
    }

    [Fact]
    public async Task A_caller_who_presents_nothing_is_never_counted_as_guessing()
    {
        var api = server.CreateApi();

        for (var i = 0; i < 30; i++)
        {
            using var refused = await api.PostAsync(Negotiate(OrbitMeshHubNames.Control), Anonymous);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        using var fine = await api.PostAsync(Negotiate(OrbitMeshHubNames.Consumer), Caller.Key(Doors.Consumer, Accounts.MachineAll.Key));
        Assert.Equal(HttpStatusCode.OK, fine.StatusCode);
    }

    // --- How the Console and a script actually connect.

    [Fact]
    public async Task The_console_connects_with_its_cookie_and_its_identity_in_the_url()
    {
        // The Console's hub URLs carry EdgeName/PackageName in the query string (a WebSocket can't set headers), and the
        // negotiation is a POST on a cookie: the rule that a cookie-borne POST needs real identity headers is for the REST
        // API, not for this.
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        var cookieOnly = new Caller(null, null, SessionToken: token);

        using var control = await api.PostAsync(Negotiate(OrbitMeshHubNames.Control, "&EdgeName=Controller&PackageName=ControlCenter"), cookieOnly);
        using var consumer = await api.PostAsync(Negotiate(OrbitMeshHubNames.Consumer, "&EdgeName=Consumer&PackageName=ControlCenter"), cookieOnly);

        Assert.Equal(HttpStatusCode.OK, control.StatusCode);
        Assert.Equal(HttpStatusCode.OK, consumer.StatusCode);
        Assert.True(await HubProbe.ConnectsAsync(server, OrbitMeshHubNames.Control, cookieOnly, query: "?EdgeName=Controller&PackageName=ControlCenter"));
    }

    [Fact]
    public async Task A_cookie_is_refused_when_the_connection_comes_from_another_origin()
    {
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);

        using var foreign = await api.PostAsync(Negotiate(OrbitMeshHubNames.Consumer), Caller.Session(token, Doors.Consumer) with { Origin = "https://evil.example" });

        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
    }

    [Fact]
    public async Task A_script_connects_to_the_control_hub_with_a_bearer_token_and_no_names()
    {
        var api = server.CreateApi();
        var admin = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        using var created = await api.PostAsync("rest/management/credentials/tokens", Caller.Session(admin), new { Name = "hub-script", Scopes = new[] { OrbitMeshScope.EdgesRead } });
        var token = (string)JsonNode.Parse(await created.Content.ReadAsStringAsync())!["Token"]!;
        await ServerFixture.EventuallyAsync(() => server.Options.CurrentValue.Credentials.Any(c => c.Name == "hub-script"), "the token should have been reloaded");

        Assert.True(await HubProbe.ConnectsAsync(server, OrbitMeshHubNames.Control, Caller.Bearer(token)));
        Assert.True(await HubProbe.ConnectsAsync(server, OrbitMeshHubNames.Consumer, Caller.Bearer(token)));
        Assert.False(await HubProbe.ConnectsAsync(server, OrbitMeshHubNames.Edge, Caller.Bearer(token)));
    }

    // --- The scope an Edge or a package needs just to connect.

    [Fact]
    public async Task An_edge_or_a_package_needs_its_connection_scope_to_connect()
    {
        // Both scopes are added at approval and by a start-up migration; they can only be missing after a hand edit, which
        // is exactly when the rule must hold.
        var api = server.CreateApi();
        var cipher = server.Services.GetRequiredService<AccessKeyCipher>();
        const string keyWithout = "key-hub-without-7a2f1c90", keyWith = "key-hub-with-5e8d3b14";
        var configuration = server.ReadConfigurationFile();
        var credentials = configuration["OrbitMesh"]!["credentials"]!.AsArray();
        credentials.Add(new JsonObject { ["name"] = "hub-edge-without", ["accessKey"] = cipher.Encrypt(keyWithout), ["kind"] = "Machine", ["enable"] = true, ["scopes"] = new JsonArray(OrbitMeshScope.EdgesRead) });
        credentials.Add(new JsonObject { ["name"] = "hub-edge-with", ["accessKey"] = cipher.Encrypt(keyWith), ["kind"] = "Machine", ["enable"] = true, ["scopes"] = new JsonArray(OrbitMeshScope.EdgeConnect, OrbitMeshScope.PackageConnect) });
        var edges = configuration["OrbitMesh"]!["edges"]!.AsArray();
        edges.Add(new JsonObject { ["name"] = "edge-without", ["credential"] = "hub-edge-without", ["packages"] = new JsonArray(new JsonObject { ["name"] = "pkg" }) });
        edges.Add(new JsonObject { ["name"] = "edge-with", ["credential"] = "hub-edge-with", ["packages"] = new JsonArray(new JsonObject { ["name"] = "pkg" }) });
        await File.WriteAllTextAsync(Path.Combine(server.ContentRoot, "appsettings.json"), configuration.ToJsonString());
        await ServerFixture.EventuallyAsync(() => server.Options.CurrentValue.Edges.Any(e => e.Name == "edge-with"), "the hand edit should have been reloaded");

        try
        {
            async Task<HttpStatusCode> AskAsync(string hub, Caller caller)
            {
                using var response = await api.PostAsync(Negotiate(hub), caller);
                return response.StatusCode;
            }

            Assert.Equal(HttpStatusCode.OK, await AskAsync(OrbitMeshHubNames.Edge, Caller.Key("edge-with", keyWith, "Edge")));
            Assert.Equal(HttpStatusCode.OK, await AskAsync(OrbitMeshHubNames.OrbitMesh, Caller.Key("edge-with", keyWith, "pkg")));
            Assert.Equal(HttpStatusCode.Forbidden, await AskAsync(OrbitMeshHubNames.Edge, Caller.Key("edge-without", keyWithout, "Edge")));
            Assert.Equal(HttpStatusCode.Forbidden, await AskAsync(OrbitMeshHubNames.OrbitMesh, Caller.Key("edge-without", keyWithout, "pkg")));
        }
        finally
        {
            // The server is shared with the other tests of this class: leave its configuration as it was found.
            var cleaned = server.ReadConfigurationFile();
            var cleanedEdges = cleaned["OrbitMesh"]!["edges"]!.AsArray();
            foreach (var name in new[] { "edge-without", "edge-with" })
            {
                cleanedEdges.Remove(cleanedEdges.Single(e => (string?)e!["name"] == name));
            }
            var cleanedCredentials = cleaned["OrbitMesh"]!["credentials"]!.AsArray();
            foreach (var name in new[] { "hub-edge-without", "hub-edge-with" })
            {
                cleanedCredentials.Remove(cleanedCredentials.Single(c => (string?)c!["name"] == name));
            }
            await File.WriteAllTextAsync(Path.Combine(server.ContentRoot, "appsettings.json"), cleaned.ToJsonString());
            await ServerFixture.EventuallyAsync(() => server.Options.CurrentValue.Edges.All(e => e.Name != "edge-with"), "the cleanup should have been reloaded");
        }
    }
}
