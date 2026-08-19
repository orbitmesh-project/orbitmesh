using OrbitMesh;
using OrbitMesh.Edge.Configuration;
using OrbitMesh.Edge.Services;
using OrbitMesh.Updating;
using NLog.Extensions.Logging;

// Relative paths in appsettings.json resolve against this - pin it to the app's own directory so it
// doesn't depend on how the process was launched (a Windows Service has no working directory of its
// own; the SCM defaults it to System32).
Environment.CurrentDirectory = AppContext.BaseDirectory;

// CLI control mode: `OrbitMesh.Edge <Start|Stop|Restart|Reload> <PackageName>` talks to an
// already-running Edge instance over the named pipe instead of starting a new one.
if (args.Length == 2 && Enum.TryParse<PackageControlAction>(args[0], ignoreCase: true, out _))
{
    var sent = EdgeManager.SendControlActionToRunningInstance(args[0], args[1]);
    Console.WriteLine(sent ? "Command sent." : "Unable to reach a running Edge instance.");
    return;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService();
builder.Services.AddSystemd();

builder.Logging.ClearProviders();
builder.Logging.AddNLog();

builder.Services.Configure<EdgeOptions>(builder.Configuration.GetSection(EdgeOptions.SectionName));
builder.Services.AddHttpClient(nameof(EdgeManager));
builder.Services.AddSingleton<EdgeManager>();
builder.Services.AddHostedService<EdgeHostedService>();

builder.Services.AddSingleton<ReleaseServerClient>();
builder.Services.AddSingleton<ReleaseVerifier>();
builder.Services.AddSingleton<EdgeSelfUpdater>();
builder.Services.AddSingleton<EdgeUpdateCheckService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<EdgeUpdateCheckService>());
var updateClientUserAgent = $"OrbitMesh.Edge-Updater/{typeof(Program).Assembly.GetName().Version}";
builder.Services.AddHttpClient(nameof(ReleaseServerClient)).ConfigureHttpClient(c => c.DefaultRequestHeaders.UserAgent.ParseAdd(updateClientUserAgent));
builder.Services.AddHttpClient(nameof(EdgeSelfUpdater)).ConfigureHttpClient(c => c.DefaultRequestHeaders.UserAgent.ParseAdd(updateClientUserAgent));

var host = builder.Build();
await host.RunAsync();
