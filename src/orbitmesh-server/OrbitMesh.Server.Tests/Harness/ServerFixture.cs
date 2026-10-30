using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OrbitMesh.Server.Configuration;
using OrbitMesh.Server.Services;

namespace OrbitMesh.Server.Tests.Harness;

/// <summary>A real OrbitMesh Server, started in-process on a temporary content root (its own appsettings.json and
/// keys/ folder), with the accounts of <see cref="Accounts"/>. One per test class (xunit class fixture); a test
/// class that needs a different configuration derives from this and overrides <see cref="Customize"/>.</summary>
public class ServerFixture : IDisposable
{
    // The content root has to be handed to the host through the environment (the configuration is read while the
    // application builds itself, before any hook of the test host runs), so servers start one at a time.
    private static readonly SemaphoreSlim StartGate = new(1, 1);
    private static int ipCounter;

    private readonly OrbitMeshServer factory;

    public string ContentRoot { get; }

    public IServiceProvider Services => factory.Services;

    public TestServer Server => factory.Server;

    public IOptionsMonitor<OrbitMeshOptions> Options => Services.GetRequiredService<IOptionsMonitor<OrbitMeshOptions>>();

    public SessionStore Sessions => Services.GetRequiredService<SessionStore>();

    public ServerFixture()
    {
        DevFilesGuard.Capture();
        ContentRoot = Path.Combine(Path.GetTempPath(), "orbitmesh-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ContentRoot);
        var configuration = Accounts.BuildConfiguration(ContentRoot, Customize);
        File.WriteAllText(Path.Combine(ContentRoot, "appsettings.json"), configuration.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        StartGate.Wait();
        var previousAspNet = Environment.GetEnvironmentVariable("ASPNETCORE_CONTENTROOT");
        var previousDotnet = Environment.GetEnvironmentVariable("DOTNET_CONTENTROOT");
        try
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_CONTENTROOT", ContentRoot);
            Environment.SetEnvironmentVariable("DOTNET_CONTENTROOT", ContentRoot);
            factory = new OrbitMeshServer(ContentRoot);
            _ = factory.Server;
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_CONTENTROOT", previousAspNet);
            Environment.SetEnvironmentVariable("DOTNET_CONTENTROOT", previousDotnet);
            StartGate.Release();
        }

        var actualRoot = Services.GetRequiredService<IHostEnvironment>().ContentRootPath;
        if (!string.Equals(Path.GetFullPath(actualRoot).TrimEnd('\\', '/'), Path.GetFullPath(ContentRoot).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
        {
            factory.Dispose();
            throw new InvalidOperationException($"The test server did not use its temporary content root ({actualRoot} instead of {ContentRoot}).");
        }
    }

    /// <summary>Adjusts the "OrbitMesh" section of the configuration this server starts with.</summary>
    protected virtual void Customize(JsonObject orbitmesh)
    {
    }

    /// <summary>A client with its own (fake) remote address, so one test's failed attempts can't lock another out.</summary>
    public Api CreateApi() => new(this, $"10.{Interlocked.Increment(ref ipCounter) % 250}.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}");

    public HttpClient CreateHttpClient(string ip)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestRemoteIp.HeaderName, ip);
        return client;
    }

    /// <summary>A single-sign-on session, as the OpenID Connect callback would have created it.</summary>
    public string CreateFederatedSession(string login, params string[] groups) =>
        Sessions.Create(OidcRoleMapper.SessionNamePrefix + login, isFederated: true, groups, "10.200.0.1").Token;

    public JsonNode ReadConfigurationFile() => JsonNode.Parse(File.ReadAllText(Path.Combine(ContentRoot, "appsettings.json")))!;

    /// <summary>The configuration is reloaded by a file watcher a moment after a write: wait for the live value.</summary>
    public static async Task EventuallyAsync(Func<bool> condition, string because, int seconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }
            await Task.Delay(100);
        }
        Assert.True(condition(), because);
    }

    public virtual void Dispose()
    {
        factory.Dispose();
        try
        {
            Directory.Delete(ContentRoot, recursive: true);
        }
        catch (IOException)
        {
            // A file watcher can still hold the folder for a moment; it's a temp folder.
        }
        DevFilesGuard.AssertUntouched();
        GC.SuppressFinalize(this);
    }

    private sealed class OrbitMeshServer(string contentRoot) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(contentRoot);
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, TestRemoteIp>());
        }
    }

    /// <summary>The test server has no network, so no remote address: take it from a header the test sets.</summary>
    private sealed class TestRemoteIp : IStartupFilter
    {
        public const string HeaderName = "X-Test-Remote-Ip";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(HeaderName, out var value) && IPAddress.TryParse(value, out var ip))
                {
                    context.Connection.RemoteIpAddress = ip;
                }
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
