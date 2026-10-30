using System.Net;
using System.Text.Json.Nodes;
using OrbitMesh.Server.Tests.Harness;

namespace OrbitMesh.Server.Tests;

/// <summary>A server with two static sites: one open to everybody, one for the local machine only.</summary>
public sealed class SitesServer : ServerFixture
{
    public const string IndexHtml = "<html><body>the index</body></html>";

    protected override void Customize(JsonObject orbitmesh)
    {
        var root = Path.GetDirectoryName(orbitmesh["packagesRootDirectory"]!.GetValue<string>())!;
        foreach (var name in new[] { "open", "private" })
        {
            var directory = Path.Combine(root, "sites", name);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "index.html"), IndexHtml);
            File.WriteAllText(Path.Combine(directory, "hello.txt"), $"hello from {name}");
        }
        orbitmesh["fileServers"] = new JsonArray
        {
            new JsonObject { ["enable"] = true, ["path"] = "/open", ["physicalPath"] = Path.Combine(root, "sites", "open"), ["isSpa"] = true, ["localhostOnly"] = false },
            new JsonObject { ["enable"] = true, ["path"] = "/private", ["physicalPath"] = Path.Combine(root, "sites", "private"), ["isSpa"] = true, ["localhostOnly"] = true }
        };
    }
}

/// <summary>Static sites are public - no AccessKey, no session - except that a site marked LocalhostOnly answers only the
/// machine it runs on. They are served before authorization, so none of the credential machinery touches them.</summary>
public sealed class FileServerTests(SitesServer server) : IClassFixture<SitesServer>
{
    private static readonly Caller Nobody = new(null, null);

    private Api Local() => new(server, "127.0.0.1");

    [Fact]
    public async Task A_public_site_is_served_to_anybody_without_a_credential()
    {
        var api = server.CreateApi();

        using var file = await api.GetAsync("open/hello.txt", Nobody);
        using var index = await api.GetAsync("open/", Nobody);

        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("hello from open", await file.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
    }

    [Fact]
    public async Task A_single_page_apps_routes_fall_back_to_its_index_without_a_credential()
    {
        var api = server.CreateApi();

        using var response = await api.GetAsync("open/some/client/side/route", Nobody);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(SitesServer.IndexHtml, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_localhost_only_site_refuses_every_other_address_even_with_a_valid_credential()
    {
        var remote = server.CreateApi();
        var admin = await remote.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);

        using var anonymous = await remote.GetAsync("private/hello.txt", Nobody);
        using var withKey = await remote.GetAsync("private/hello.txt", Caller.Key(Doors.Consumer, Accounts.MachineAll.Key));
        using var withSession = await remote.GetAsync("private/hello.txt", Caller.Session(admin, Doors.Consumer));
        using var route = await remote.GetAsync("private/some/route", Nobody);

        Assert.Equal(HttpStatusCode.Forbidden, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, withKey.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, withSession.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, route.StatusCode);
    }

    [Fact]
    public async Task A_localhost_only_site_is_served_to_the_local_machine()
    {
        var local = Local();

        using var file = await local.GetAsync("private/hello.txt", Nobody);
        using var route = await local.GetAsync("private/some/route", Nobody);

        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("hello from private", await file.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, route.StatusCode);
    }

    [Fact]
    public async Task Static_files_never_look_at_credentials_so_a_bad_key_there_counts_for_nothing()
    {
        var api = server.CreateApi();

        for (var i = 0; i < 30; i++)
        {
            using var response = await api.GetAsync("open/hello.txt", Caller.Key(Doors.Consumer, "a-wrong-key"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var fine = await api.GetAsync(Doors.ConsumerGate, Caller.Key(Doors.Consumer, Accounts.MachineAll.Key));
        Assert.Equal(HttpStatusCode.OK, fine.StatusCode);
    }

    [Fact]
    public async Task The_api_is_still_closed_next_to_the_sites()
    {
        var api = server.CreateApi();

        using var anonymous = await api.GetAsync("rest/management/credentials", Nobody);
        using var redirect = await api.GetAsync("open", Nobody);

        Assert.Equal(HttpStatusCode.Forbidden, anonymous.StatusCode);
        // A bare site path gets a trailing slash so the site's relative links resolve.
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
    }
}
