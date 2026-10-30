using System.Net;
using System.Text.Json.Nodes;
using OrbitMesh.Server.Services;
using OrbitMesh.Server.Tests.Harness;

namespace OrbitMesh.Server.Tests;

/// <summary>What happens to people who are already signed in when their account changes, and how secrets are kept
/// at rest. Each test that changes an account uses one of its own (see <see cref="Accounts"/>).</summary>
public sealed class AccountLifecycleTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    [Fact]
    public async Task Disabling_an_account_signs_the_person_out_and_keeps_them_out()
    {
        var api = server.CreateApi();
        var admin = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        var victim = await api.SignInAsync(Accounts.DisableMe.Name, Accounts.DisableMe.Password);
        using (var before = await api.GetAsync(Doors.ManagementGate, Caller.Session(victim)))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        }

        using var disable = await api.PostAsync("rest/management/credentials", Caller.Session(admin), new
        {
            Name = Accounts.DisableMe.Name,
            AccessKey = "",
            Kind = "Human",
            Enable = false,
            Scopes = Accounts.DisableMe.Scopes
        });

        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        await ServerFixture.EventuallyAsync(
            () => server.Options.CurrentValue.Credentials.Single(c => c.Name == Accounts.DisableMe.Name).Enable == false,
            "the change should have been reloaded");
        using var me = await api.GetAsync("rest/auth/me", Caller.Session(victim));
        using var management = await api.GetAsync(Doors.ManagementGate, Caller.Session(victim));
        using var again = await api.LoginAsync(Accounts.DisableMe.Name, Accounts.DisableMe.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, management.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, again.StatusCode);
    }

    [Fact]
    public async Task Resetting_someones_password_ends_their_sessions_and_the_old_password()
    {
        var api = server.CreateApi();
        var admin = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        var stolen = await api.SignInAsync(Accounts.ResetMe.Name, Accounts.ResetMe.Password);
        const string newPassword = "Brand-New-Passw0rd-9";

        using var reset = await api.PostAsync("rest/management/credentials", Caller.Session(admin), new
        {
            Name = Accounts.ResetMe.Name,
            AccessKey = newPassword,
            Kind = "Human",
            Enable = true,
            Scopes = Accounts.ResetMe.Scopes
        });

        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        using var staleSession = await api.GetAsync("rest/auth/me", Caller.Session(stolen));
        Assert.Equal(HttpStatusCode.Unauthorized, staleSession.StatusCode);
        // The administrator who did the reset is not signed out.
        using var adminStillIn = await api.GetAsync("rest/auth/me", Caller.Session(admin));
        Assert.Equal(HttpStatusCode.OK, adminStillIn.StatusCode);

        await ServerFixture.EventuallyAsync(
            () => PasswordHasher.Verify(server.Options.CurrentValue.Credentials.Single(c => c.Name == Accounts.ResetMe.Name).AccessKey, newPassword),
            "the new password should have been reloaded");
        using var old = await api.LoginAsync(Accounts.ResetMe.Name, Accounts.ResetMe.Password);
        using var fresh = await api.LoginAsync(Accounts.ResetMe.Name, newPassword);
        Assert.Equal(HttpStatusCode.Forbidden, old.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
    }

    [Fact]
    public async Task Resetting_your_own_password_keeps_you_signed_in_and_ends_your_other_sessions()
    {
        var api = server.CreateApi();
        var here = await api.SignInAsync(Accounts.Manager.Name, Accounts.Manager.Password);
        var elsewhere = await api.SignInAsync(Accounts.Manager.Name, Accounts.Manager.Password);

        using var reset = await api.PostAsync("rest/management/credentials", Caller.Session(here), new
        {
            Name = Accounts.Manager.Name,
            AccessKey = "Another-Passw0rd-7",
            Kind = "Human",
            Enable = true,
            Scopes = Accounts.Manager.Scopes
        });

        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        using var hereAfter = await api.GetAsync("rest/auth/me", Caller.Session(here));
        using var elsewhereAfter = await api.GetAsync("rest/auth/me", Caller.Session(elsewhere));
        Assert.Equal(HttpStatusCode.OK, hereAfter.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, elsewhereAfter.StatusCode);
    }

    [Fact]
    public async Task Editing_an_account_without_a_new_password_leaves_its_sessions_alone()
    {
        var api = server.CreateApi();
        var admin = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        var person = await api.SignInAsync(Accounts.EditMe.Name, Accounts.EditMe.Password);

        using var edit = await api.PostAsync("rest/management/credentials", Caller.Session(admin), new
        {
            Name = Accounts.EditMe.Name,
            AccessKey = "",
            Kind = "Human",
            Enable = true,
            Scopes = Accounts.EditMe.Scopes
        });

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        using var after = await api.GetAsync("rest/auth/me", Caller.Session(person));
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    // --- Secrets at rest.

    [Fact]
    public async Task A_new_person_is_stored_as_a_password_hash_never_as_typed()
    {
        var api = server.CreateApi();
        var admin = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        const string password = "Typed-Passw0rd-123";

        using var create = await api.PostAsync("rest/management/credentials", Caller.Session(admin), new
        {
            Name = "newcomer",
            AccessKey = password,
            Kind = "Human",
            Enable = true,
            Scopes = new[] { OrbitMeshScope.EdgesRead }
        });

        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var file = await File.ReadAllTextAsync(Path.Combine(server.ContentRoot, "appsettings.json"));
        Assert.DoesNotContain(password, file, StringComparison.Ordinal);
        var stored = (string)server.ReadConfigurationFile()["OrbitMesh"]!["credentials"]!.AsArray().Single(c => (string?)c!["name"] == "newcomer")!["accessKey"]!;
        Assert.StartsWith("PWD$600000$", stored, StringComparison.Ordinal);
        await ServerFixture.EventuallyAsync(() => server.Options.CurrentValue.Credentials.Any(c => c.Name == "newcomer"), "the new account should have been reloaded");
        using var signIn = await api.LoginAsync("newcomer", password);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
    }

    [Fact]
    public async Task A_new_machine_key_is_stored_encrypted_and_still_works()
    {
        var api = server.CreateApi();
        var admin = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        const string key = "machine-key-for-a-new-dashboard-42";

        using var create = await api.PostAsync("rest/management/credentials", Caller.Session(admin), new
        {
            Name = "dashboard",
            AccessKey = key,
            Kind = "Machine",
            Enable = true,
            Scopes = new[] { OrbitMeshScope.TelemetryRead }
        });

        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var file = await File.ReadAllTextAsync(Path.Combine(server.ContentRoot, "appsettings.json"));
        Assert.DoesNotContain(key, file, StringComparison.Ordinal);
        await ServerFixture.EventuallyAsync(() => server.Options.CurrentValue.Credentials.Any(c => c.Name == "dashboard"), "the new credential should have been reloaded");
        using var consumer = await api.GetAsync(Doors.ConsumerGate, Caller.Key(Doors.Consumer, key));
        Assert.Equal(HttpStatusCode.OK, consumer.StatusCode);
    }

    [Fact]
    public void Everything_written_at_startup_is_already_in_its_protected_form()
    {
        var credentials = server.ReadConfigurationFile()["OrbitMesh"]!["credentials"]!.AsArray();

        foreach (var credential in credentials)
        {
            var stored = (string)credential!["accessKey"]!;
            var kind = (string)credential["kind"]!;
            Assert.True(kind == "Human" ? stored.StartsWith("PWD$", StringComparison.Ordinal) || stored.StartsWith("PBKDF2$", StringComparison.Ordinal) : stored.StartsWith("ENC$", StringComparison.Ordinal),
                $"'{(string)credential["name"]!}' ({kind}) holds an unprotected secret");
        }
    }

    // --- Accounts from before sessions existed.

    [Fact]
    public async Task An_old_account_signs_in_once_and_is_upgraded_to_a_real_password_hash()
    {
        var api = server.CreateApi();
        var legacy = Accounts.Legacy;
        var oldDerivedKey = PasswordHasher.LegacyAccessKey(legacy.Name, legacy.Password);
        Assert.StartsWith("PBKDF2$", server.Options.CurrentValue.Credentials.Single(c => c.Name == legacy.Name).AccessKey, StringComparison.Ordinal);
        // Before: an old Console or script that sends the derived key as its AccessKey still gets in.
        using (var withOldKey = await api.GetAsync(Doors.ConsumerGate, Caller.Key(Doors.Consumer, oldDerivedKey)))
        {
            Assert.Equal(HttpStatusCode.OK, withOldKey.StatusCode);
        }

        using var signIn = await api.LoginAsync(legacy.Name, legacy.Password);

        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        await ServerFixture.EventuallyAsync(
            () => server.Options.CurrentValue.Credentials.Single(c => c.Name == legacy.Name).AccessKey.StartsWith("PWD$600000$", StringComparison.Ordinal),
            "the hash should have been upgraded at the first sign-in");
        // After: the derived key is no longer a bearer token, the password still signs in.
        using var withOldKeyAfter = await api.GetAsync(Doors.ConsumerGate, Caller.Key(Doors.Consumer, oldDerivedKey));
        Assert.Equal(HttpStatusCode.Forbidden, withOldKeyAfter.StatusCode);
        using var again = await api.LoginAsync(legacy.Name, legacy.Password);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Fact]
    public async Task A_person_account_cannot_be_used_as_an_access_key_once_it_has_a_password_hash()
    {
        // The hash is what the old Console would have used as a bearer: it must no longer be one.
        var api = server.CreateApi();
        var stored = server.Options.CurrentValue.Credentials.Single(c => c.Name == Accounts.Viewer.Name).AccessKey;

        using var withHash = await api.GetAsync(Doors.ConsumerGate, Caller.Key(Doors.Consumer, stored));
        using var withPassword = await api.GetAsync(Doors.ConsumerGate, Caller.Key(Doors.Consumer, Accounts.Viewer.Password));
        using var withDerivedKey = await api.GetAsync(Doors.ConsumerGate, Caller.Key(Doors.Consumer, PasswordHasher.LegacyAccessKey(Accounts.Viewer.Name, Accounts.Viewer.Password)));

        Assert.Equal(HttpStatusCode.Forbidden, withHash.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, withPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, withDerivedKey.StatusCode);
    }
}
