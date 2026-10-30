using System.Net;
using System.Text.Json.Nodes;
using OrbitMesh.Server.Services;
using OrbitMesh.Server.Tests.Harness;

namespace OrbitMesh.Server.Tests;

/// <summary>The raw configuration editor can rewrite anything the typed endpoints refuse, so it carries its own
/// guards: secrets are masked on the way out and restored on the way back, nobody can hand out scopes they do not
/// hold, nobody can lock themselves out, and a few settings that would break the server are refused.</summary>
public sealed class ConfigurationGuardTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private const string Mask = "********";
    private const string ConfigurationPath = "rest/management/configuration";

    // --- Secrets in transit.

    [Fact]
    public async Task Reading_the_configuration_masks_every_secret()
    {
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Viewer.Name, Accounts.Viewer.Password);

        using var response = await api.GetAsync(ConfigurationPath, Caller.Session(token));

        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("PWD$", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ENC$", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PBKDF2$", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Accounts.OidcClientSecret, text, StringComparison.Ordinal);
        foreach (var machine in Accounts.AllMachines)
        {
            Assert.DoesNotContain(machine.Key, text, StringComparison.Ordinal);
        }
        var document = JsonNode.Parse(text)!;
        Assert.Equal(Mask, (string?)document["auth"]!["oidc"]!["clientSecret"]);
        Assert.All(document["credentials"]!.AsArray(), c => Assert.Equal(Mask, (string?)c!["accessKey"]));
    }

    [Fact]
    public async Task Saving_the_masked_document_back_changes_no_secret()
    {
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        var before = server.ReadConfigurationFile()["OrbitMesh"]!;
        var document = await ReadConfigurationAsync(api, token);

        using var save = await api.PostAsync(ConfigurationPath, Caller.Session(token), document.ToJsonString());

        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var after = server.ReadConfigurationFile()["OrbitMesh"]!;
        Assert.Equal((string?)before["auth"]!["oidc"]!["clientSecret"], (string?)after["auth"]!["oidc"]!["clientSecret"]);
        Assert.Equal(
            before["credentials"]!.AsArray().Select(c => (string)c!["accessKey"]!).Order(),
            after["credentials"]!.AsArray().Select(c => (string)c!["accessKey"]!).Order());
    }

    [Fact]
    public async Task A_mask_with_nothing_behind_it_is_refused_rather_than_stored()
    {
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        var document = await ReadConfigurationAsync(api, token);
        document["credentials"]!.AsArray().Add(new JsonObject
        {
            ["name"] = "invented", ["accessKey"] = Mask, ["kind"] = "Machine", ["enable"] = true, ["scopes"] = new JsonArray(OrbitMeshScope.EdgesRead)
        });

        using var save = await api.PostAsync(ConfigurationPath, Caller.Session(token), document.ToJsonString());

        Assert.Equal(HttpStatusCode.BadRequest, save.StatusCode);
        Assert.DoesNotContain(server.Options.CurrentValue.Credentials, c => c.Name == "invented");
    }

    [Fact]
    public async Task A_new_client_secret_replaces_the_stored_one()
    {
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        var document = await ReadConfigurationAsync(api, token);
        document["auth"]!["oidc"]!["clientSecret"] = "the-rotated-client-secret";

        using var save = await api.PostAsync(ConfigurationPath, Caller.Session(token), document.ToJsonString());

        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal("the-rotated-client-secret", (string?)server.ReadConfigurationFile()["OrbitMesh"]!["auth"]!["oidc"]!["clientSecret"]);
    }

    // --- Who may write, and what.

    [Fact]
    public async Task Writing_the_configuration_needs_the_write_scope()
    {
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Viewer.Name, Accounts.Viewer.Password);
        var document = await ReadConfigurationAsync(api, token);

        using var save = await api.PostAsync(ConfigurationPath, Caller.Session(token), document.ToJsonString());

        Assert.Equal(HttpStatusCode.Forbidden, save.StatusCode);
    }

    [Fact]
    public async Task Being_refused_is_not_being_signed_out()
    {
        // The Console tells "refused" from "no session" by asking the Server whether the session still exists;
        // that only works if a refusal leaves the session alone.
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Viewer.Name, Accounts.Viewer.Password);

        using var refused = await api.PostAsync(ConfigurationPath, Caller.Session(token), "{}");
        using var me = await api.GetAsync("rest/auth/me", Caller.Session(token));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Nobody_can_hand_out_scopes_they_do_not_hold()
    {
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.ConfigWriter.Name, Accounts.ConfigWriter.Password);
        var document = await ReadConfigurationAsync(api, token);
        document["credentials"]!.AsArray().Add(new JsonObject
        {
            ["name"] = "promoted", ["accessKey"] = "ENC$AAAA$AAAA$AAAA", ["kind"] = "Machine", ["enable"] = true,
            ["scopes"] = new JsonArray(OrbitMeshScope.CredentialsManage)
        });

        using var save = await api.PostAsync(ConfigurationPath, Caller.Session(token), document.ToJsonString());

        Assert.Equal(HttpStatusCode.Forbidden, save.StatusCode);
        Assert.Contains("don't hold", await save.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(server.Options.CurrentValue.Credentials, c => c.Name == "promoted");
    }

    [Fact]
    public async Task A_plaintext_secret_cannot_be_written_through_the_editor()
    {
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        var document = await ReadConfigurationAsync(api, token);
        document["credentials"]!.AsArray().Add(new JsonObject
        {
            ["name"] = "plain", ["accessKey"] = "a-plaintext-key", ["kind"] = "Machine", ["enable"] = true, ["scopes"] = new JsonArray(OrbitMeshScope.EdgesRead)
        });

        using var save = await api.PostAsync(ConfigurationPath, Caller.Session(token), document.ToJsonString());

        Assert.Equal(HttpStatusCode.BadRequest, save.StatusCode);
        Assert.Contains("plaintext", await save.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/rest")]
    [InlineData("/signalr")]
    [InlineData("/metrics")]
    [InlineData("/packages")]
    [InlineData("/")]
    [InlineData("no-leading-slash")]
    public async Task A_static_site_cannot_shadow_the_api_or_break_routing(string path)
    {
        // File servers are served without authentication, so one on /rest would be an open door.
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        var document = await ReadConfigurationAsync(api, token);
        document["fileServers"]!.AsArray().Add(new JsonObject { ["enable"] = true, ["path"] = path, ["physicalPath"] = server.ContentRoot, ["isSpa"] = false });

        using var save = await api.PostAsync(ConfigurationPath, Caller.Session(token), document.ToJsonString());

        Assert.Equal(HttpStatusCode.BadRequest, save.StatusCode);
    }

    // --- Nobody can lock themselves (or everybody) out.

    [Fact]
    public async Task A_local_account_cannot_remove_its_own_write_access()
    {
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.ConfigWriter.Name, Accounts.ConfigWriter.Password);
        var document = await ReadConfigurationAsync(api, token);
        var self = document["credentials"]!.AsArray().Single(c => (string?)c!["name"] == Accounts.ConfigWriter.Name)!;
        self["scopes"] = new JsonArray(OrbitMeshScope.ConfigurationRead);

        using var save = await api.PostAsync(ConfigurationPath, Caller.Session(token), document.ToJsonString());

        Assert.Equal(HttpStatusCode.BadRequest, save.StatusCode);
        Assert.Contains("revoke your own", await save.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_single_sign_on_user_cannot_unmap_the_only_group_that_lets_them_in()
    {
        var api = server.CreateApi();
        var token = server.CreateFederatedSession("grace", Accounts.ConfigGroup);
        var document = await ReadConfigurationAsync(api, token);
        var mappings = document["auth"]!["oidc"]!["roleMappings"]!.AsArray();
        mappings.Remove(mappings.Single(m => (string?)m!["group"] == Accounts.ConfigGroup));

        using var save = await api.PostAsync(ConfigurationPath, Caller.Session(token), document.ToJsonString());

        Assert.Equal(HttpStatusCode.BadRequest, save.StatusCode);
        Assert.Contains("single-sign-on", await save.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Local_sign_in_cannot_be_turned_off_while_single_sign_on_is_not_usable()
    {
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        var document = await ReadConfigurationAsync(api, token);
        document["auth"]!["localLoginEnabled"] = false;
        document["auth"]!["oidc"]!["enable"] = false;

        using var save = await api.PostAsync(ConfigurationPath, Caller.Session(token), document.ToJsonString());

        Assert.Equal(HttpStatusCode.BadRequest, save.StatusCode);
        Assert.True(server.Options.CurrentValue.Auth.LocalLoginEnabled);
    }

    // --- Single sign-on role mappings are credentials in all but name.

    [Fact]
    public async Task A_person_cannot_map_a_group_to_scopes_they_do_not_hold()
    {
        var api = server.CreateApi();
        var token = server.CreateFederatedSession("heidi", Accounts.ConfigGroup);
        var document = await ReadConfigurationAsync(api, token);
        document["auth"]!["oidc"]!["roleMappings"]!.AsArray().Add(new JsonObject { ["group"] = "heidis-friends", ["scopes"] = new JsonArray("*") });

        using var save = await api.PostAsync(ConfigurationPath, Caller.Session(token), document.ToJsonString());

        Assert.Equal(HttpStatusCode.Forbidden, save.StatusCode);
        Assert.DoesNotContain(server.Options.CurrentValue.Auth.Oidc.RoleMappings, m => m.Group == "heidis-friends");
    }

    [Fact]
    public async Task An_administrator_can_map_a_group_to_every_scope()
    {
        // Regression (server 1.3.2): "*" used to include edge:connect and package:connect, which an Administrator
        // account does not hold - so the Administrator was refused.
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        var document = await ReadConfigurationAsync(api, token);
        document["auth"]!["oidc"]!["roleMappings"]!.AsArray().Add(new JsonObject { ["group"] = "new-admins", ["scopes"] = new JsonArray("*") });

        using var save = await api.PostAsync(ConfigurationPath, Caller.Session(token), document.ToJsonString());

        Assert.True(save.IsSuccessStatusCode, $"{(int)save.StatusCode}: {await save.Content.ReadAsStringAsync()}");
        await ServerFixture.EventuallyAsync(() => server.Options.CurrentValue.Auth.Oidc.RoleMappings.Any(m => m.Group == "new-admins"), "the mapping should have been reloaded");
        var granted = OidcRoleMapper.ScopesFor(server.Options.CurrentValue.Auth.Oidc, ["new-admins"]);
        Assert.Equal(OrbitMeshScope.ForPeople.Order(), granted.Order());
    }

    [Fact]
    public async Task The_machine_connection_scopes_are_never_granted_to_a_person_through_a_mapping()
    {
        var api = server.CreateApi();
        var token = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        var document = await ReadConfigurationAsync(api, token);
        document["auth"]!["oidc"]!["roleMappings"]!.AsArray().Add(new JsonObject
        {
            ["group"] = "would-be-edges", ["scopes"] = new JsonArray(OrbitMeshScope.EdgeConnect, OrbitMeshScope.PackageConnect, OrbitMeshScope.EdgesRead)
        });

        using var save = await api.PostAsync(ConfigurationPath, Caller.Session(token), document.ToJsonString());

        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        await ServerFixture.EventuallyAsync(() => server.Options.CurrentValue.Auth.Oidc.RoleMappings.Any(m => m.Group == "would-be-edges"), "the mapping should have been reloaded");
        var granted = OidcRoleMapper.ScopesFor(server.Options.CurrentValue.Auth.Oidc, ["would-be-edges"]);
        Assert.Equal([OrbitMeshScope.EdgesRead], granted);
    }

    private static async Task<JsonNode> ReadConfigurationAsync(Api api, string token)
    {
        using var response = await api.GetAsync(ConfigurationPath, Caller.Session(token));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }
}
