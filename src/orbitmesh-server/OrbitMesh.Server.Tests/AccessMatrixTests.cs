using System.Net;
using OrbitMesh.Server.Tests.Harness;

namespace OrbitMesh.Server.Tests;

/// <summary>Which identity gets through which door - the table of the security documentation, as tests.
/// A door is an EdgeName a caller presents (Management, Controller, Consumer, an Edge's own name, Developer);
/// the gate is what the Server decides before any endpoint-specific scope is looked at.</summary>
public sealed class AccessMatrixTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private const string EdgePackage = "Edge";

    public static IEnumerable<object[]> MachineDoors()
    {
        // Management and Controller: any enabled credential holding at least one scope. The scope each
        // endpoint then needs is a separate check (see ScopeEnforcementTests).
        yield return Row("management: no key", Doors.Management, null, Doors.ManagementGate, HttpStatusCode.Forbidden);
        yield return Row("management: wrong key", Doors.Management, "not-a-real-key", Doors.ManagementGate, HttpStatusCode.Forbidden);
        yield return Row("management: all scopes", Doors.Management, Accounts.MachineAll.Key, Doors.ManagementGate, HttpStatusCode.OK);
        yield return Row("management: two scopes", Doors.Management, Accounts.MachineRead.Key, Doors.ManagementGate, HttpStatusCode.OK);
        yield return Row("management: enabled but no scope", Doors.Management, Accounts.MachineNoScope.Key, Doors.ManagementGate, HttpStatusCode.Forbidden);
        yield return Row("management: disabled credential", Doors.Management, Accounts.MachineDisabled.Key, Doors.ManagementGate, HttpStatusCode.Forbidden);
        yield return Row("management: an edge's credential (one scope, so the gate opens)", Doors.Management, Accounts.EdgeA.Key, Doors.ManagementGate, HttpStatusCode.OK);

        yield return Row("controller: all scopes", Doors.Controller, Accounts.MachineAll.Key, Doors.ControllerGate, HttpStatusCode.OK);
        yield return Row("controller: no key", Doors.Controller, null, Doors.ControllerGate, HttpStatusCode.Forbidden);
        yield return Row("controller: enabled but no scope", Doors.Controller, Accounts.MachineNoScope.Key, Doors.ControllerGate, HttpStatusCode.Forbidden);

        // Consumer: any enabled credential, scopes or not (what it may then see is the authorization rules' business).
        yield return Row("consumer: no key", Doors.Consumer, null, Doors.ConsumerGate, HttpStatusCode.Forbidden);
        yield return Row("consumer: wrong key", Doors.Consumer, "not-a-real-key", Doors.ConsumerGate, HttpStatusCode.Forbidden);
        yield return Row("consumer: no scope is enough", Doors.Consumer, Accounts.MachineNoScope.Key, Doors.ConsumerGate, HttpStatusCode.OK);
        yield return Row("consumer: read-only credential", Doors.Consumer, Accounts.MachineRead.Key, Doors.ConsumerGate, HttpStatusCode.OK);
        yield return Row("consumer: disabled credential", Doors.Consumer, Accounts.MachineDisabled.Key, Doors.ConsumerGate, HttpStatusCode.Forbidden);

        // An Edge's own door: only the credential assigned to that Edge opens it.
        yield return Row("edge-a: its own credential", "edge-a", Accounts.EdgeA.Key, Doors.OrbitMeshGate, HttpStatusCode.OK);
        yield return Row("edge-a: another edge's credential", "edge-a", Accounts.EdgeB.Key, Doors.OrbitMeshGate, HttpStatusCode.Forbidden);
        yield return Row("edge-a: a powerful credential that isn't assigned to it", "edge-a", Accounts.MachineAll.Key, Doors.OrbitMeshGate, HttpStatusCode.Forbidden);
        yield return Row("edge-a: no key", "edge-a", null, Doors.OrbitMeshGate, HttpStatusCode.Forbidden);
        yield return Row("an edge that isn't declared", "edge-x", Accounts.EdgeA.Key, Doors.OrbitMeshGate, HttpStatusCode.Forbidden);

        // The developer edge: a credential that holds the Developer scope.
        yield return Row("developer: holds the scope", Doors.Developer, Accounts.MachineAll.Key, Doors.OrbitMeshGate, HttpStatusCode.OK);
        yield return Row("developer: lacks the scope", Doors.Developer, Accounts.MachineRead.Key, Doors.OrbitMeshGate, HttpStatusCode.Forbidden);
    }

    private static object[] Row(string label, string edge, string? key, string path, HttpStatusCode expected) => [label, edge, key!, path, expected];

    [Theory]
    [MemberData(nameof(MachineDoors))]
    public async Task A_machine_credential_gets_through_the_doors_it_is_meant_for(string label, string edge, string? key, string path, HttpStatusCode expected)
    {
        var api = server.CreateApi();
        var caller = key == null ? Caller.Anonymous(edge) : Caller.Key(edge, key, edge.StartsWith("edge-", StringComparison.Ordinal) ? EdgePackage : "test");

        using var response = await api.GetAsync(path, caller);

        Assert.True(response.StatusCode == expected, $"{label}: expected {(int)expected}, got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task A_package_that_the_edge_does_not_declare_is_refused()
    {
        var api = server.CreateApi();

        using var response = await api.GetAsync(Doors.OrbitMeshGate, Caller.Key("edge-a", Accounts.EdgeA.Key, package: "ghost-package"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_valid_key_without_an_EdgeName_is_refused()
    {
        var api = server.CreateApi();

        using var response = await api.GetAsync(Doors.ConsumerGate, new Caller(EdgeName: null, PackageName: "test", AccessKey: Accounts.MachineAll.Key));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_identity_can_travel_in_the_query_string_as_well_as_in_headers()
    {
        // The documented wire contract the Edge, the SDKs and the dashboards rely on (SignalR WebSockets can't
        // set headers, so they use the query string).
        var api = server.CreateApi();
        var path = $"{Doors.ConsumerGate}?EdgeName={Doors.Consumer}&PackageName=test&AccessKey={Accounts.MachineRead.Key}";

        using var response = await api.GetAsync(path, new Caller(EdgeName: null, PackageName: null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("rest/auth/config")]
    [InlineData("rest/management/setup/status")]
    public async Task Some_endpoints_are_public_by_design(string path)
    {
        var api = server.CreateApi();

        using var response = await api.GetAsync(path, new Caller(EdgeName: null, PackageName: null));

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task Enrollment_is_reachable_without_a_credential()
    {
        // An Edge that isn't approved yet has no key to present - that is the whole point of rest/enroll.
        var api = server.CreateApi();

        using var response = await api.GetAsync($"rest/enroll/{Guid.NewGuid()}/status", new Caller(EdgeName: null, PackageName: null));

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Everything_else_under_rest_needs_a_credential()
    {
        var api = server.CreateApi();
        var anonymous = new Caller(EdgeName: null, PackageName: null);

        foreach (var path in new[] { "rest/management/credentials", "rest/management/configuration", "rest/consumer/CheckAccess", "rest/orbitmesh/CheckAccess", "rest/controller/CheckAccess" })
        {
            using var response = await api.GetAsync(path, anonymous);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{path} answered {(int)response.StatusCode} to an anonymous caller");
        }
    }
}
