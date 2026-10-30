using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrbitMesh.Server.Tests.Harness;

namespace OrbitMesh.Server.Tests;

/// <summary>The harness itself: a test server must live in its own temporary folder and never touch the
/// developer's real configuration (see <see cref="DevFilesGuard"/>).</summary>
public sealed class IsolationTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    [Fact]
    public void The_server_runs_on_its_temporary_content_root()
    {
        var root = server.Services.GetRequiredService<IHostEnvironment>().ContentRootPath;

        Assert.Equal(Path.GetFullPath(server.ContentRoot).TrimEnd('\\', '/'), Path.GetFullPath(root).TrimEnd('\\', '/'), ignoreCase: true);
        Assert.True(File.Exists(Path.Combine(server.ContentRoot, "keys", "credential-encryption.key")));
    }

    [Fact]
    public void The_developers_own_files_are_untouched()
    {
        DevFilesGuard.AssertUntouched();
    }

    [Fact]
    public void The_server_starts_with_the_configuration_the_harness_wrote()
    {
        var options = server.Options.CurrentValue;

        Assert.Contains(options.Credentials, c => c.Name == Accounts.Admin.Name);
        Assert.Equal(Accounts.AllMachines.Length + Accounts.AllHumans.Length, options.Credentials.Count);
        Assert.True(options.Auth.Oidc.IsUsable);
    }

    [Fact]
    public async Task Starting_the_server_wrote_nothing_to_its_configuration()
    {
        // Machine keys are seeded already encrypted and the Edge credentials already hold edge:connect,
        // so there is nothing to migrate at startup - the file is exactly what the harness wrote.
        var api = server.CreateApi();
        using var response = await api.GetAsync("rest/auth/config", Caller.Anonymous(Doors.Management));

        Assert.True(response.IsSuccessStatusCode);
        Assert.False(File.Exists(Path.Combine(server.ContentRoot, "appsettings.json.bak")));
    }
}
