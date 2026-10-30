using System.Net;
using OrbitMesh.Server.Tests.Harness;

namespace OrbitMesh.Server.Tests;

/// <summary>The Prometheus endpoint is public by design (the credential gate lets it through), so all that can go
/// wrong is the exporter itself. Regression: with an exporter built for an older OpenTelemetry core than the one
/// installed, the first scrape that came after some traffic (when the request-duration histogram exists) threw
/// a TypeLoadException and answered 500 for the lifetime of the process.</summary>
public sealed class MetricsTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    [Fact]
    public async Task A_scrape_after_traffic_returns_the_metrics_including_histograms()
    {
        var api = server.CreateApi();
        var anonymous = new Caller(EdgeName: null, PackageName: null);
        using (await api.GetAsync("rest/auth/config", anonymous)) { }
        using (await api.GetAsync("rest/management/setup/status", anonymous)) { }
        using (await api.GetAsync(Doors.ConsumerGate, Caller.Key(Doors.Consumer, Accounts.MachineAll.Key))) { }

        using var first = await api.GetAsync("metrics", anonymous);
        using var second = await api.GetAsync("metrics", anonymous);

        var body = await first.Content.ReadAsStringAsync();
        Assert.True(first.StatusCode == HttpStatusCode.OK, $"first scrape answered {(int)first.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("# TYPE", body, StringComparison.Ordinal);
        Assert.Contains("_bucket{", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_metrics_endpoint_needs_no_credential()
    {
        var api = server.CreateApi();

        using var response = await api.GetAsync("metrics", new Caller(EdgeName: null, PackageName: null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
