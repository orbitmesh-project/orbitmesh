using System.Net;
using Microsoft.Extensions.DependencyInjection;
using OrbitMesh.Server.Services;
using OrbitMesh.Server.Tests.Harness;
using static OrbitMesh.MessageScope;

namespace OrbitMesh.Server.Tests;

/// <summary>The last layer: once a credential is through its door and holds the scope, its own rules narrow what
/// it can address (messages, groups) and see (telemetry items). A rule only matters when it goes against the
/// default, and the first rule that does decides.</summary>
public sealed class AuthorizationRulesTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private IOrbitMeshDirectory Directory => server.Services.GetRequiredService<IOrbitMeshDirectory>();

    private static string Closed => Accounts.RulesClosed.Key;

    private static string Open => Accounts.RulesOpen.Key;

    // --- Messages: who a credential may address.

    [Theory]
    [InlineData(ScopeType.Package, "lights", true)]
    [InlineData(ScopeType.Package, "LIGHTS", true)]
    [InlineData(ScopeType.Package, "heater", false)]
    [InlineData(ScopeType.Group, "living-room", true)]
    [InlineData(ScopeType.Group, "kitchen", false)]
    [InlineData(ScopeType.All, null, false)]
    [InlineData(ScopeType.Others, null, false)]
    [InlineData(ScopeType.Edge, "edge-a", false)]
    public void A_closed_credential_reaches_only_what_it_was_allowed(ScopeType scope, string? target, bool expected)
    {
        var addressed = target == null ? new MessageScope(scope) : new MessageScope(scope, target);

        Assert.Equal(expected, Directory.CheckMessageAuthorization(Closed, addressed, "Ping"));
    }

    [Fact]
    public void A_rule_can_close_one_message_key_on_an_otherwise_open_credential()
    {
        var package = new MessageScope(ScopeType.Package, "heater");

        Assert.True(Directory.CheckMessageAuthorization(Open, package, "Ping"));
        Assert.False(Directory.CheckMessageAuthorization(Open, package, "Reboot"));
        Assert.False(Directory.CheckMessageAuthorization(Open, package, "reboot"));
        Assert.True(Directory.CheckMessageAuthorization(Open, new MessageScope(ScopeType.Group, "any"), "Reboot"));
    }

    [Fact]
    public void A_credential_with_no_rules_is_open_and_an_unknown_key_is_closed()
    {
        var scope = new MessageScope(ScopeType.All);

        Assert.True(Directory.CheckMessageAuthorization(Accounts.MachineAll.Key, scope, "Ping"));
        Assert.False(Directory.CheckMessageAuthorization("not-a-real-key", scope, "Ping"));
        Assert.False(Directory.CheckMessageAuthorization(null, scope, "Ping"));
        Assert.False(Directory.CheckMessageAuthorization(Accounts.MachineDisabled.Key, scope, "Ping"));
        Assert.False(Directory.CheckMessageAuthorization(Open, null, "Ping"));
        Assert.False(Directory.CheckMessageAuthorization(Open, scope, ""));
    }

    [Fact]
    public void A_forged_saga_response_does_not_bypass_the_rules()
    {
        // A "__Response" message with a made-up saga id is not something this server saw go out: the rules apply
        // (here: not even a rule can let it through - an unknown saga is refused outright).
        var forged = new MessageScope(ScopeType.Package, "heater") { SagaId = "made-up-saga" };

        Assert.False(Directory.CheckMessageAuthorization(Closed, forged, OrbitMeshDefaultNames.SagaResponseMessageKey));
    }

    // --- Groups.

    [Theory]
    [InlineData("living-room", true)]
    [InlineData("kitchen", false)]
    [InlineData("Living-Room", false)]
    public void A_closed_credential_joins_only_the_groups_it_was_allowed(string group, bool expected)
    {
        Assert.Equal(expected, Directory.CheckMessageGroupAuthorization(Closed, group));
    }

    [Fact]
    public void Group_checks_refuse_unknown_keys_and_empty_names()
    {
        Assert.False(Directory.CheckMessageGroupAuthorization("not-a-real-key", "living-room"));
        Assert.False(Directory.CheckMessageGroupAuthorization(Closed, ""));
        Assert.True(Directory.CheckMessageGroupAuthorization(Accounts.MachineAll.Key, "anything"));
    }

    // --- Telemetry items: an open credential with holes.

    [Theory]
    [InlineData("edge-a", "pkg", "Temperature", true)]
    [InlineData("secret-edge", "pkg", "Temperature", false)]
    [InlineData("SECRET-EDGE", "pkg", "Temperature", false)]
    [InlineData("edge-a", "pkg", "Password", false)]
    [InlineData("edge-a", "pkg", "password", false)]
    [InlineData("secret-edge", "pkg", "Password", false)]
    public void An_open_credential_sees_everything_but_what_was_excluded(string edge, string package, string item, bool expected)
    {
        Assert.Equal(expected, Directory.CheckTelemetryItemAuthorization(Closed, edge, package, item));
    }

    [Fact]
    public void A_telemetry_check_refuses_an_unknown_key()
    {
        Assert.False(Directory.CheckTelemetryItemAuthorization("not-a-real-key", "edge-a", "pkg", "Temperature"));
        Assert.False(Directory.CheckTelemetryItemAuthorization(null, "edge-a", "pkg", "Temperature"));
    }

    // --- The same rules, reached through the REST door.

    [Fact]
    public async Task Over_REST_an_allowed_message_goes_out_and_a_denied_one_does_not()
    {
        var api = server.CreateApi();
        var caller = Caller.Key(Doors.Consumer, Closed);

        using var allowed = await api.GetAsync("rest/consumer/SendMessage?scope=Package&key=Ping&args=lights", caller);
        using var denied = await api.GetAsync("rest/consumer/SendMessage?scope=Package&key=Ping&args=heater", caller);

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);
    }

    [Fact]
    public async Task Sending_a_message_needs_the_messages_scope_before_any_rule_is_looked_at()
    {
        var api = server.CreateApi();

        // m-read has no messages:execute: "default allow" must not be enough to send.
        using var response = await api.GetAsync("rest/consumer/SendMessage?scope=All&key=Ping", Caller.Key(Doors.Consumer, Accounts.MachineRead.Key));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_person_with_a_session_is_not_subject_to_a_credentials_rules_but_to_scopes()
    {
        // Rules belong to credentials; a person's session is governed by scopes alone.
        var api = server.CreateApi();
        var admin = await api.SignInAsync(Accounts.Admin.Name, Accounts.Admin.Password);
        var viewer = await api.SignInAsync(Accounts.Viewer.Name, Accounts.Viewer.Password);

        using var asAdmin = await api.GetAsync("rest/consumer/SendMessage?scope=All&key=Ping", Caller.Session(admin, Doors.Consumer));
        using var asViewer = await api.GetAsync("rest/consumer/SendMessage?scope=All&key=Ping", Caller.Session(viewer, Doors.Consumer));

        Assert.Equal(HttpStatusCode.OK, asAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asViewer.StatusCode);
    }
}
