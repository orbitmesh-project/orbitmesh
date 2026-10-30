using System.Net;
using System.Text.Json.Nodes;
using OrbitMesh.Server.Services;
using OrbitMesh.Server.Tests.Harness;

namespace OrbitMesh.Server.Tests;

// Servers started with a different configuration than the default one.

public sealed class NoLocalLoginServer : ServerFixture
{
    protected override void Customize(JsonObject orbitmesh) => orbitmesh["auth"]!["localLoginEnabled"] = false;
}

public sealed class NoOidcServer : ServerFixture
{
    protected override void Customize(JsonObject orbitmesh) => orbitmesh["auth"]!["oidc"]!["enable"] = false;
}

/// <summary>A brand-new install: no credential, no single sign-on.</summary>
public sealed class FreshInstallServer : ServerFixture
{
    protected override void Customize(JsonObject orbitmesh)
    {
        orbitmesh["credentials"] = new JsonArray();
        orbitmesh["edges"] = new JsonArray();
        orbitmesh["auth"]!["oidc"]!["enable"] = false;
    }
}

/// <summary>A brand-new install that relies on single sign-on from the start.</summary>
public sealed class FreshOidcServer : ServerFixture
{
    protected override void Customize(JsonObject orbitmesh)
    {
        orbitmesh["credentials"] = new JsonArray();
        orbitmesh["edges"] = new JsonArray();
    }
}

public sealed class NoLocalLoginTests(NoLocalLoginServer server) : IClassFixture<NoLocalLoginServer>
{
    [Fact]
    public async Task The_sign_in_page_is_told_the_password_form_is_off()
    {
        var api = server.CreateApi();

        using var response = await api.GetAsync("rest/auth/config", Caller.Anonymous(Doors.Management));

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.False((bool)body["LocalLoginEnabled"]!);
        Assert.True((bool)body["Oidc"]!["Enabled"]!);
    }

    [Fact]
    public async Task A_password_sign_in_is_refused_even_with_the_right_password()
    {
        var api = server.CreateApi();

        using var response = await api.LoginAsync(Accounts.Admin.Name, Accounts.Admin.Password);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(Api.SessionCookieHeaderOf(response));
    }

    [Fact]
    public async Task Machines_and_single_sign_on_are_unaffected()
    {
        var api = server.CreateApi();
        var sso = server.CreateFederatedSession("alice", Accounts.AdminGroup);

        using var machine = await api.GetAsync(Doors.ConsumerGate, Caller.Key(Doors.Consumer, Accounts.MachineAll.Key));
        using var person = await api.GetAsync(Doors.ManagementGate, Caller.Session(sso));

        Assert.Equal(HttpStatusCode.OK, machine.StatusCode);
        Assert.Equal(HttpStatusCode.OK, person.StatusCode);
    }
}

public sealed class NoOidcTests(NoOidcServer server) : IClassFixture<NoOidcServer>
{
    [Fact]
    public async Task The_sign_in_page_is_told_there_is_no_single_sign_on()
    {
        var api = server.CreateApi();

        using var response = await api.GetAsync("rest/auth/config", Caller.Anonymous(Doors.Management));

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.True((bool)body["LocalLoginEnabled"]!);
        Assert.False((bool)body["Oidc"]!["Enabled"]!);
    }

    [Fact]
    public async Task Starting_a_single_sign_on_is_not_found()
    {
        var api = server.CreateApi();

        using var response = await api.GetAsync("rest/auth/oidc/login?returnUrl=/", Caller.Anonymous(Doors.Management));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_single_sign_on_session_is_worthless_when_the_provider_is_off()
    {
        var api = server.CreateApi();
        var token = server.CreateFederatedSession("alice", Accounts.AdminGroup);

        using var me = await api.GetAsync("rest/auth/me", Caller.Session(token));
        using var management = await api.GetAsync(Doors.ManagementGate, Caller.Session(token));

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, management.StatusCode);
    }
}

/// <summary>Turning single sign-on off ends the sessions it signed in, live.</summary>
public sealed class ProviderRemovalTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    [Fact]
    public async Task Disabling_the_provider_in_the_configuration_ends_its_open_sessions()
    {
        var api = server.CreateApi();
        var token = server.CreateFederatedSession("alice", Accounts.AdminGroup);
        using (var before = await api.GetAsync(Doors.ManagementGate, Caller.Session(token)))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        }

        var configuration = server.ReadConfigurationFile();
        configuration["OrbitMesh"]!["auth"]!["oidc"]!["enable"] = false;
        await File.WriteAllTextAsync(Path.Combine(server.ContentRoot, "appsettings.json"), configuration.ToJsonString());

        await ServerFixture.EventuallyAsync(() => !server.Options.CurrentValue.Auth.Oidc.IsUsable, "the change should have been reloaded");
        using var after = await api.GetAsync(Doors.ManagementGate, Caller.Session(token));
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
    }
}

public sealed class FreshInstallTests(FreshInstallServer server) : IClassFixture<FreshInstallServer>
{
    [Fact]
    public async Task First_run_creates_the_one_administrator_and_then_closes_for_good()
    {
        var api = server.CreateApi();
        const string password = "Boss-Passw0rd-1";
        Assert.True(await NeedsSetupAsync(api));

        using var blank = await api.PostAsync("rest/management/setup/create-admin", Caller.Anonymous(Doors.Management), new { Name = " ", Password = password });
        using var ssoName = await api.PostAsync("rest/management/setup/create-admin", Caller.Anonymous(Doors.Management), new { Name = "oidc:boss", Password = password });
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, ssoName.StatusCode);

        using var create = await api.PostAsync("rest/management/setup/create-admin", Caller.Anonymous(Doors.Management), new { Name = "boss", Password = password });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        await ServerFixture.EventuallyAsync(() => server.Options.CurrentValue.Credentials.Count == 1, "the administrator should have been reloaded");
        Assert.False(await NeedsSetupAsync(api));

        // The window is closed: a second visitor can't create another all-powerful account.
        using var second = await api.PostAsync("rest/management/setup/create-admin", Caller.Anonymous(Doors.Management), new { Name = "intruder", Password = "Intruder-Passw0rd-1" });
        Assert.False(second.IsSuccessStatusCode);
        Assert.Single(server.Options.CurrentValue.Credentials);

        // The administrator is stored as a password hash and can sign in and administer.
        Assert.StartsWith("PWD$600000$", server.Options.CurrentValue.Credentials.Single().AccessKey, StringComparison.Ordinal);
        var token = await api.SignInAsync("boss", password);
        using var me = await api.GetAsync("rest/auth/me", Caller.Session(token));
        var scopes = ((JsonArray)JsonNode.Parse(await me.Content.ReadAsStringAsync())!["Scopes"]!).Select(s => (string)s!).ToList();
        // Every scope a person can hold - exactly what a single-sign-on administrator gets from a "*" mapping.
        Assert.Equal(OrbitMeshScope.ForPeople.Order(), scopes.Order());

        // And the last credential can't be removed, which would reopen first-run setup to the next visitor.
        using var remove = await api.SendAsync(HttpMethod.Delete, "rest/management/credentials/boss", Caller.Session(token));
        Assert.Equal(HttpStatusCode.Conflict, remove.StatusCode);
        Assert.Single(server.Options.CurrentValue.Credentials);
    }

    private static async Task<bool> NeedsSetupAsync(Api api)
    {
        using var response = await api.GetAsync("rest/management/setup/status", Caller.Anonymous(Doors.Management));
        return (bool)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["NeedsSetup"]!;
    }
}

/// <summary>A Console that was not updated together with the Server still finishes the first-run setup the old way:
/// it sends the SHA-1 it computes itself as an AccessKey.</summary>
public sealed class LegacyFirstRunTests(FreshInstallServer server) : IClassFixture<FreshInstallServer>
{
    [Fact]
    public async Task An_old_console_can_still_create_the_first_administrator()
    {
        var api = server.CreateApi();
        const string password = "Old-Console-Passw0rd-1";

        using var create = await api.PostAsync("rest/management/setup/create-admin", Caller.Anonymous(Doors.Management),
            new { Name = "boss", AccessKey = PasswordHasher.LegacyAccessKey("boss", password) });

        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        await ServerFixture.EventuallyAsync(() => server.Options.CurrentValue.Credentials.Count == 1, "the administrator should have been reloaded");
        Assert.StartsWith("PBKDF2$", server.Options.CurrentValue.Credentials.Single().AccessKey, StringComparison.Ordinal);
        using var signIn = await api.LoginAsync("boss", password);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
    }
}

public sealed class FreshOidcTests(FreshOidcServer server) : IClassFixture<FreshOidcServer>
{
    [Fact]
    public async Task There_is_no_first_run_setup_when_single_sign_on_decides_who_administers()
    {
        var api = server.CreateApi();

        using var status = await api.GetAsync("rest/management/setup/status", Caller.Anonymous(Doors.Management));
        using var create = await api.PostAsync("rest/management/setup/create-admin", Caller.Anonymous(Doors.Management), new { Name = "boss", Password = "Boss-Passw0rd-1" });

        Assert.False((bool)JsonNode.Parse(await status.Content.ReadAsStringAsync())!["NeedsSetup"]!);
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Empty(server.Options.CurrentValue.Credentials);
    }

    [Fact]
    public async Task The_groups_alone_decide_who_can_administer()
    {
        var api = server.CreateApi();
        var admin = server.CreateFederatedSession("alice", Accounts.AdminGroup);
        var stranger = server.CreateFederatedSession("mallory", Accounts.UnmappedGroup);

        using var allowed = await api.GetAsync(Doors.ManagementGate, Caller.Session(admin));
        using var refused = await api.GetAsync(Doors.ManagementGate, Caller.Session(stranger));

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }
}
