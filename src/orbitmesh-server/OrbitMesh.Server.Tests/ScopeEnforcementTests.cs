using System.Net;
using OrbitMesh.Server.Tests.Harness;

namespace OrbitMesh.Server.Tests;

/// <summary>Past the gate, each Management endpoint needs its own scope. Three read endpoints, three
/// credentials that hold exactly one scope each: every credential opens its own endpoint and no other.</summary>
public sealed class ScopeEnforcementTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static readonly (MachineAccount Holder, string Path)[] Endpoints =
    [
        (Accounts.OnlyConfigRead, "rest/management/configuration"),
        (Accounts.OnlyCredentialsRead, "rest/management/credentials"),
        (Accounts.OnlyEdgesRead, "rest/management/edges")
    ];

    public static IEnumerable<object[]> Combinations() =>
        from holder in Endpoints
        from endpoint in Endpoints
        select new object[] { holder.Holder.Name, endpoint.Path, holder.Holder == endpoint.Holder };

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task An_endpoint_needs_exactly_its_own_scope(string holderName, string path, bool shouldPass)
    {
        var holder = Accounts.AllMachines.Single(m => m.Name == holderName);
        var api = server.CreateApi();

        using var response = await api.GetAsync(path, Caller.Key(Doors.Management, holder.Key));

        Assert.Equal(shouldPass ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_credential_with_every_scope_reaches_all_of_them()
    {
        var api = server.CreateApi();

        foreach (var (_, path) in Endpoints)
        {
            using var response = await api.GetAsync(path, Caller.Key(Doors.Management, Accounts.MachineAll.Key));
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} answered {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task Writing_needs_the_write_scope_even_when_reading_is_allowed()
    {
        var api = server.CreateApi();
        var reader = Caller.Key(Doors.Management, Accounts.OnlyCredentialsRead.Key);

        using var response = await api.PostAsync("rest/management/credentials", reader, new
        {
            Name = "sneaky",
            AccessKey = "a-key-1234567890",
            Kind = "Machine",
            Enable = true,
            Scopes = new[] { "credentials:read" }
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(server.Options.CurrentValue.Credentials, c => c.Name == "sneaky");
    }
}
