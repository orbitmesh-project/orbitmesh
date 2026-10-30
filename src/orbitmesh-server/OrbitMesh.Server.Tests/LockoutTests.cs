using System.Net;
using OrbitMesh.Server.Tests.Harness;

namespace OrbitMesh.Server.Tests;

/// <summary>Brute-force protection: ten failed guesses in five minutes lock the source address out. Only a request
/// that actually presented a secret is a guess; a caller with no credential, or a stale cookie, is merely
/// unauthenticated and must not be able to lock a person out of their own address. Every test uses its own
/// (fake) remote address.</summary>
public sealed class LockoutTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private const int Threshold = 10;

    [Fact]
    public async Task Repeated_wrong_access_keys_lock_the_address_out_even_for_a_valid_key()
    {
        var api = server.CreateApi();
        var guess = Caller.Key(Doors.Consumer, "a-guess-that-is-wrong");

        for (var i = 0; i < Threshold; i++)
        {
            using var denied = await api.GetAsync(Doors.ConsumerGate, guess);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        using var locked = await api.GetAsync(Doors.ConsumerGate, Caller.Key(Doors.Consumer, Accounts.MachineAll.Key));
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
    }

    [Fact]
    public async Task A_lockout_only_concerns_the_address_that_earned_it()
    {
        var offender = server.CreateApi();
        var bystander = server.CreateApi();

        for (var i = 0; i < Threshold; i++)
        {
            using var denied = await offender.GetAsync(Doors.ConsumerGate, Caller.Key(Doors.Consumer, "wrong"));
        }

        using var fine = await bystander.GetAsync(Doors.ConsumerGate, Caller.Key(Doors.Consumer, Accounts.MachineAll.Key));
        Assert.Equal(HttpStatusCode.OK, fine.StatusCode);
    }

    [Fact]
    public async Task Repeated_wrong_passwords_lock_the_address_out_even_for_the_right_password()
    {
        var api = server.CreateApi();

        for (var i = 0; i < Threshold; i++)
        {
            using var denied = await api.LoginAsync(Accounts.Admin.Name, "wrong-password");
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        using var locked = await api.LoginAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Null(Api.SessionCookieHeaderOf(locked));
    }

    [Fact]
    public async Task Wrong_passwords_and_wrong_keys_count_together()
    {
        var api = server.CreateApi();

        for (var i = 0; i < Threshold / 2; i++)
        {
            using var password = await api.LoginAsync(Accounts.Admin.Name, "wrong-password");
            using var key = await api.GetAsync(Doors.ConsumerGate, Caller.Key(Doors.Consumer, "wrong"));
        }

        using var locked = await api.GetAsync(Doors.ConsumerGate, Caller.Key(Doors.Consumer, Accounts.MachineAll.Key));
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
    }

    [Fact]
    public async Task A_success_does_not_reset_the_count()
    {
        // Otherwise an attacker holding one valid key could interleave a success among their guesses forever.
        var api = server.CreateApi();

        for (var round = 0; round < 2; round++)
        {
            for (var i = 0; i < Threshold / 2; i++)
            {
                using var denied = await api.GetAsync(Doors.ConsumerGate, Caller.Key(Doors.Consumer, "wrong"));
            }
            using var success = await api.GetAsync(Doors.ConsumerGate, Caller.Key(Doors.Consumer, Accounts.MachineAll.Key));
            if (round == 0)
            {
                Assert.Equal(HttpStatusCode.OK, success.StatusCode);
            }
        }

        using var locked = await api.GetAsync(Doors.ConsumerGate, Caller.Key(Doors.Consumer, Accounts.MachineAll.Key));
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
    }

    [Fact]
    public async Task Requests_without_any_credential_never_lock_anyone_out()
    {
        var api = server.CreateApi();

        for (var i = 0; i < Threshold * 3; i++)
        {
            using var denied = await api.GetAsync(Doors.ManagementGate, Caller.Anonymous(Doors.Management));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        using var fine = await api.GetAsync(Doors.ConsumerGate, Caller.Key(Doors.Consumer, Accounts.MachineAll.Key));
        Assert.Equal(HttpStatusCode.OK, fine.StatusCode);
    }

    [Fact]
    public async Task A_stale_session_cookie_never_locks_anyone_out()
    {
        // The Console keeps retrying with the cookie of a session that just expired: that must not lock the person
        // out of their own address, who is about to sign in again.
        var api = server.CreateApi();

        for (var i = 0; i < Threshold * 3; i++)
        {
            using var denied = await api.GetAsync(Doors.ManagementGate, Caller.Session("an-expired-or-forged-token"));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        using var signIn = await api.LoginAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
    }

    [Fact]
    public async Task The_lockout_is_visible_to_an_administrator_and_can_be_lifted()
    {
        var offender = server.CreateApi();
        var admin = server.CreateApi();
        for (var i = 0; i < Threshold; i++)
        {
            using var denied = await offender.GetAsync(Doors.ConsumerGate, Caller.Key(Doors.Consumer, "wrong"));
        }
        var token = await admin.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);

        using var list = await admin.GetAsync("rest/management/security/lockouts", Caller.Session(token));

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains(offender.Ip, await list.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using (var stillLocked = await offender.GetAsync(Doors.ConsumerGate, Caller.Key(Doors.Consumer, Accounts.MachineAll.Key)))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, stillLocked.StatusCode);
        }
        using var unlock = await admin.SendAsync(HttpMethod.Delete, $"rest/management/security/lockouts/{offender.Ip}", Caller.Session(token));
        Assert.Equal(HttpStatusCode.OK, unlock.StatusCode);
        using var freed = await offender.GetAsync(Doors.ConsumerGate, Caller.Key(Doors.Consumer, Accounts.MachineAll.Key));
        Assert.Equal(HttpStatusCode.OK, freed.StatusCode);
    }
}
