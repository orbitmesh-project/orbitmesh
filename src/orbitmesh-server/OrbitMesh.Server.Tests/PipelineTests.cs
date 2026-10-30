using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OrbitMesh.Server.Security;
using OrbitMesh.Server.Tests.Harness;

namespace OrbitMesh.Server.Tests;

/// <summary>The shape of the authorization pipeline rather than one rule of it: every endpoint is either declared
/// public on purpose or protected, every Management action says which scope it needs, and whatever matches no endpoint
/// is refused to an anonymous caller. These fail when someone adds an endpoint and forgets to decide.</summary>
public sealed class PipelineTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private IEnumerable<RouteEndpoint> Endpoints => server.Services.GetServices<EndpointDataSource>()
        .SelectMany(s => s.Endpoints)
        .OfType<RouteEndpoint>();

    private static string Pattern(RouteEndpoint endpoint) => (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');

    private static bool IsPublic(Endpoint endpoint) => endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null;

    // --- The inventory of what is public on purpose.

    [Fact]
    public void Only_the_endpoints_that_are_meant_to_be_public_are_public()
    {
        var unexpected = Endpoints
            .Where(IsPublic)
            .Select(Pattern)
            .Where(pattern =>
                !pattern.StartsWith("rest/auth/", StringComparison.OrdinalIgnoreCase)                // sign-in, sign-out, who am I, single sign-on
                && !pattern.StartsWith("rest/enroll", StringComparison.OrdinalIgnoreCase)            // an Edge asking to be approved
                && !pattern.StartsWith("rest/management/setup/", StringComparison.OrdinalIgnoreCase) // first run, closes itself
                && !pattern.StartsWith("signalr/hubs/", StringComparison.OrdinalIgnoreCase)          // hubs decide in OnConnectedAsync
                && !pattern.Equals("metrics", StringComparison.OrdinalIgnoreCase)                    // Prometheus
                && !pattern.Equals("openapi/{documentName}.json", StringComparison.OrdinalIgnoreCase)
                && !pattern.Contains("{*path:nonfile}", StringComparison.Ordinal))                   // a static site's SPA fallback
            .Distinct()
            .ToList();

        Assert.True(unexpected.Count == 0, "These endpoints are public - if that is intended, add them to this list: " + string.Join(", ", unexpected));
    }

    [Fact]
    public void Every_other_endpoint_declares_a_door()
    {
        var undeclared = Endpoints
            .Where(e => !IsPublic(e) && !e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any())
            .Select(Pattern)
            .Distinct()
            .ToList();

        // Not a hole (the fallback policy still refuses them), but a decision somebody should make explicitly.
        Assert.True(undeclared.Count == 0, "These endpoints declare neither [AllowAnonymous] nor a policy: " + string.Join(", ", undeclared));
    }

    [Fact]
    public void Every_management_action_says_which_scope_it_needs()
    {
        var withoutScope = Endpoints
            .Where(e => Pattern(e).StartsWith("rest/management/", StringComparison.OrdinalIgnoreCase) && !IsPublic(e))
            .Where(e => !e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy?.StartsWith("scope:", StringComparison.Ordinal) == true))
            .Select(Pattern)
            .Distinct()
            .Order()
            .ToList();

        // These need only to be let in: everyone with a Management-capable credential may ask (the licence ones are
        // stubs that tell the Console there is no licence system).
        var open = new[] { "rest/management/CheckAccess", "rest/management/server/version", "rest/management/license" };
        Assert.True(withoutScope.Except(open, StringComparer.OrdinalIgnoreCase).Count() == 0,
            "These Management actions need no scope beyond being let in - if that is intended, add them above: "
            + string.Join(", ", withoutScope.Except(open, StringComparer.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Whatever_has_no_declared_policy_is_refused_by_default()
    {
        var provider = server.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var fallback = await provider.GetFallbackPolicyAsync();

        Assert.NotNull(fallback);
        Assert.Contains(fallback.Requirements, r => r is DoorRequirement { AccessType: OrbitMeshAccessType.OrbitMesh });
    }

    // --- Paths that match no endpoint.

    [Theory]
    [InlineData("/")]
    [InlineData("/nothing-here")]
    [InlineData("/rest/nothing/here")]
    [InlineData("/rest/management/not-an-endpoint")]
    public async Task An_anonymous_caller_is_refused_everywhere_not_told_what_exists(string path)
    {
        var api = server.CreateApi();

        using var response = await api.GetAsync(path, new Caller(null, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_caller_let_in_is_told_a_missing_path_is_missing()
    {
        var api = server.CreateApi();

        using var machine = await api.GetAsync("/nothing-here", Caller.Key(Doors.Consumer, Accounts.MachineAll.Key));
        using var person = await api.GetAsync("/rest/nothing/here", Caller.Session(await api.SignInAsync(Accounts.Viewer.Name, Accounts.Viewer.Password), Doors.Consumer));

        Assert.Equal(HttpStatusCode.NotFound, machine.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, person.StatusCode);
    }

    // --- What a refusal looks like, and what it costs.

    [Fact]
    public async Task A_scope_refusal_names_the_scope_and_a_door_refusal_does_not()
    {
        var api = server.CreateApi();

        using var scope = await api.GetAsync("rest/management/credentials", Caller.Key(Doors.Management, Accounts.OnlyEdgesRead.Key));
        using var door = await api.GetAsync("rest/management/credentials", Caller.Key(Doors.Management, "not-a-real-key"));

        Assert.Equal(HttpStatusCode.Forbidden, scope.StatusCode);
        Assert.Contains("credentials:read", await scope.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, door.StatusCode);
        Assert.Equal("Forbidden", await door.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Being_refused_a_scope_is_not_guessing_and_never_locks_anyone_out()
    {
        // The door let this caller in; it merely asked for something it may not have.
        var api = server.CreateApi();

        for (var i = 0; i < 25; i++)
        {
            using var refused = await api.GetAsync("rest/management/credentials", Caller.Key(Doors.Management, Accounts.OnlyEdgesRead.Key));
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        using var fine = await api.GetAsync("rest/management/edges", Caller.Key(Doors.Management, Accounts.OnlyEdgesRead.Key));
        Assert.Equal(HttpStatusCode.OK, fine.StatusCode);
    }

    [Fact]
    public async Task A_valid_key_refused_at_the_door_does_count()
    {
        // m-noscope is a real, enabled credential - but the Management door needs one scope at least.
        var api = server.CreateApi();

        for (var i = 0; i < 10; i++)
        {
            using var refused = await api.GetAsync(Doors.ManagementGate, Caller.Key(Doors.Management, Accounts.MachineNoScope.Key));
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        using var locked = await api.GetAsync(Doors.ConsumerGate, Caller.Key(Doors.Consumer, Accounts.MachineAll.Key));
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
    }

    // --- Package downloads: an endpoint of their own, for the Edges and for those who manage packages.

    private static readonly byte[] PackageBytes = "PK-not-really-a-zip"u8.ToArray();

    private async Task PreparePackageAsync()
    {
        var directory = Path.Combine(server.ContentRoot, "packages");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "demo.zip"), PackageBytes);
    }

    [Fact]
    public async Task An_edge_downloads_its_packages_with_its_own_credential()
    {
        await PreparePackageAsync();
        var api = server.CreateApi();

        using var response = await api.GetAsync("packages/demo.zip", Caller.Key("edge-a", Accounts.EdgeA.Key, "Edge"));
        using var missing = await api.GetAsync("packages/not-there.zip", Caller.Key("edge-a", Accounts.EdgeA.Key, "Edge"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(PackageBytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Naming_the_consumer_door_no_longer_gets_a_package_without_packages_read()
    {
        // Used to work for any enabled credential - even one with no scope - and for any signed-in person.
        await PreparePackageAsync();
        var api = server.CreateApi();
        var manager = await api.SignInAsync(Accounts.Manager.Name, Accounts.Manager.Password); // credentials:*, edges:read - no packages:read

        foreach (var (label, caller) in new (string, Caller)[]
        {
            ("a machine key with no scope", Caller.Key(Doors.Consumer, Accounts.MachineNoScope.Key)),
            ("a read-only machine key without packages:read", Caller.Key(Doors.Consumer, Accounts.MachineRead.Key)),
            ("a machine key with edges:read only", Caller.Key(Doors.Consumer, Accounts.OnlyEdgesRead.Key)),
            ("a person without packages:read", Caller.Session(manager, Doors.Consumer)),
            ("an anonymous caller", Caller.Anonymous(Doors.Consumer)),
        })
        {
            using var response = await api.GetAsync("packages/demo.zip", caller);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{label} got {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task Only_the_edge_itself_may_download_as_an_edge()
    {
        await PreparePackageAsync();
        var api = server.CreateApi();

        foreach (var (label, caller) in new (string, Caller)[]
        {
            ("a package of that edge, not the edge", Caller.Key("edge-a", Accounts.EdgeA.Key, "some-package")),
            ("no package name at all", new Caller("edge-a", null, Accounts.EdgeA.Key)),
            ("another edge's credential", Caller.Key("edge-a", Accounts.EdgeB.Key, "Edge")),
            ("an edge that is not declared", Caller.Key("edge-x", Accounts.EdgeA.Key, "Edge")),
            ("a powerful key that is not assigned to the edge", Caller.Key("edge-a", Accounts.MachineAll.Key, "Edge")),
            ("a reserved door name with an edge credential", Caller.Key(Doors.Developer, Accounts.EdgeA.Key, "Edge")),
            ("a wrong key", Caller.Key("edge-a", "not-a-real-key", "Edge")),
        })
        {
            using var response = await api.GetAsync("packages/demo.zip", caller);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{label} got {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task An_edge_whose_credential_cannot_connect_as_an_edge_cannot_download_either()
    {
        // Edges get edge:connect when they are approved, and a start-up migration adds it to older ones; it can only be
        // missing after a hand edit - which is exactly when the rule must hold.
        await PreparePackageAsync();
        var api = server.CreateApi();
        const string key = "key-edge-noconnect-7d1c40e2";
        var cipher = server.Services.GetRequiredService<OrbitMesh.Server.Services.AccessKeyCipher>();
        var configuration = server.ReadConfigurationFile();
        configuration["OrbitMesh"]!["credentials"]!.AsArray().Add(new JsonObject
        {
            ["name"] = "edge-noconnect-cred", ["accessKey"] = cipher.Encrypt(key), ["kind"] = "Machine", ["enable"] = true,
            ["scopes"] = new JsonArray(OrbitMeshScope.EdgesRead)
        });
        configuration["OrbitMesh"]!["edges"]!.AsArray().Add(new JsonObject { ["name"] = "edge-noconnect", ["credential"] = "edge-noconnect-cred", ["packages"] = new JsonArray() });
        await File.WriteAllTextAsync(Path.Combine(server.ContentRoot, "appsettings.json"), configuration.ToJsonString());
        await ServerFixture.EventuallyAsync(() => server.Options.CurrentValue.Edges.Any(e => e.Name == "edge-noconnect"), "the hand edit should have been reloaded");

        try
        {
            using var response = await api.GetAsync("packages/demo.zip", Caller.Key("edge-noconnect", key, "Edge"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            // The server is shared with the other tests of this class: leave its configuration as it was found.
            var cleaned = server.ReadConfigurationFile();
            var edges = cleaned["OrbitMesh"]!["edges"]!.AsArray();
            edges.Remove(edges.Single(e => (string?)e!["name"] == "edge-noconnect"));
            var credentials = cleaned["OrbitMesh"]!["credentials"]!.AsArray();
            credentials.Remove(credentials.Single(c => (string?)c!["name"] == "edge-noconnect-cred"));
            await File.WriteAllTextAsync(Path.Combine(server.ContentRoot, "appsettings.json"), cleaned.ToJsonString());
            await ServerFixture.EventuallyAsync(() => server.Options.CurrentValue.Edges.All(e => e.Name != "edge-noconnect"), "the cleanup should have been reloaded");
        }
    }

    [Fact]
    public async Task Whoever_manages_packages_may_read_them_through_the_management_door()
    {
        await PreparePackageAsync();
        var api = server.CreateApi();
        var viewer = await api.SignInAsync(Accounts.Viewer.Name, Accounts.Viewer.Password); // holds packages:read
        var admin = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        using var created = await api.PostAsync("rest/management/credentials/tokens", Caller.Session(admin),
            new { Name = "pkg-reader", Scopes = new[] { OrbitMeshScope.PackagesRead } });
        var tokenWithRead = (string)JsonNode.Parse(await created.Content.ReadAsStringAsync())!["Token"]!;
        using var createdOther = await api.PostAsync("rest/management/credentials/tokens", Caller.Session(admin),
            new { Name = "no-pkg-read", Scopes = new[] { OrbitMeshScope.EdgesRead } });
        var tokenWithoutRead = (string)JsonNode.Parse(await createdOther.Content.ReadAsStringAsync())!["Token"]!;
        await ServerFixture.EventuallyAsync(() => server.Options.CurrentValue.Credentials.Any(c => c.Name == "no-pkg-read"), "the tokens should have been reloaded");

        using var person = await api.GetAsync("packages/demo.zip", Caller.Session(viewer));
        using var token = await api.GetAsync("packages/demo.zip", Caller.Key(Doors.Management, tokenWithRead));
        using var machine = await api.GetAsync("packages/demo.zip", Caller.Key(Doors.Management, Accounts.MachineAll.Key));
        using var withoutScope = await api.GetAsync("packages/demo.zip", Caller.Key(Doors.Management, tokenWithoutRead));
        using var edgesOnly = await api.GetAsync("packages/demo.zip", Caller.Key(Doors.Management, Accounts.OnlyEdgesRead.Key));

        Assert.Equal(HttpStatusCode.OK, person.StatusCode);
        Assert.Equal(HttpStatusCode.OK, token.StatusCode);
        Assert.Equal(HttpStatusCode.OK, machine.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, withoutScope.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, edgesOnly.StatusCode);
    }

    [Fact]
    public async Task A_refused_download_by_a_key_counts_as_a_guess()
    {
        await PreparePackageAsync();
        var api = server.CreateApi();

        for (var i = 0; i < 10; i++)
        {
            using var refused = await api.GetAsync("packages/demo.zip", Caller.Key("edge-a", "a-guess", "Edge"));
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        using var locked = await api.GetAsync("packages/demo.zip", Caller.Key("edge-a", Accounts.EdgeA.Key, "Edge"));
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
    }

    [Theory]
    [InlineData("packages/..%2fappsettings.json")]
    [InlineData("packages/%2e%2e/appsettings.json")]
    [InlineData("packages/..%5cappsettings.json")]
    [InlineData("packages//etc/passwd")]
    public async Task A_package_download_cannot_leave_the_packages_folder(string path)
    {
        var api = server.CreateApi();

        using var response = await api.GetAsync(path, Caller.Key("edge-a", Accounts.EdgeA.Key, "Edge"));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("credentials", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }
}
