using System.Text.Json.Serialization;
using OrbitMesh;
using OrbitMesh.Server;
using OrbitMesh.Server.Configuration;
using OrbitMesh.Server.Hubs;
using OrbitMesh.Server.Middleware;
using OrbitMesh.Server.Providers;
using OrbitMesh.Server.Services;
using OrbitMesh.Updating;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using NLog.Extensions.Logging;
using OpenTelemetry.Metrics;

// Relative paths in appsettings.json resolve against this - pin it to the app's own directory so it
// doesn't depend on how the process was launched (a Windows Service has no working directory of its
// own; the SCM defaults it to System32).
Environment.CurrentDirectory = AppContext.BaseDirectory;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();
builder.Host.UseSystemd();

builder.Logging.ClearProviders();
builder.Logging.AddNLog();

builder.Services.Configure<OrbitMeshOptions>(builder.Configuration.GetSection(OrbitMeshOptions.SectionName));

builder.Services.AddSingleton<IOrbitMeshDirectory, OrbitMeshDirectory>();
builder.Services.AddSingleton<IOrbitMeshConfigWriter, OrbitMeshConfigWriter>();
builder.Services.AddSingleton<IEdgeRegistry, EdgeRegistry>();
builder.Services.AddSingleton<IPendingEdgeRegistry, PendingEdgeRegistry>();
builder.Services.AddSingleton<IPackageRegistry, PackageRegistry>();
builder.Services.AddSingleton<PackageGroupManager>();
builder.Services.AddSingleton<ConsumerGroupManager>();
builder.Services.AddSingleton<ITelemetryItemProvider, InMemoryTelemetryItemProvider>();
builder.Services.AddSingleton<OrbitMeshTelemetryItemManager>();
builder.Services.AddSingleton<OrbitMeshMetrics>();
builder.Services.AddSingleton<OrbitMeshLogService>();
builder.Services.AddSingleton<LoginAttemptLimiter>();
builder.Services.AddSingleton<CredentialUsageTracker>();
builder.Services.AddSingleton<AccessKeyCipher>();
builder.Services.AddHostedService<CredentialUsageFlushService>();
builder.Services.AddSingleton<PackageRegistrySync>();
builder.Services.AddSingleton<ReleaseServerClient>();
builder.Services.AddSingleton<ReleaseVerifier>();
builder.Services.AddSingleton<UpdateStatus>();
builder.Services.AddSingleton<UpdateCheckService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<UpdateCheckService>());
builder.Services.AddSingleton<ServerSelfUpdater>();
builder.Services.AddSingleton<StaticSiteUpdateRegistry>();
builder.Services.AddSingleton<StaticSiteUpdateCheckService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<StaticSiteUpdateCheckService>());
builder.Services.AddSingleton<StaticSiteUpdater>();
builder.Services.AddSingleton<NuGetFeedClient>();
// HttpClient sends no User-Agent at all by default, unlike curl/a browser - some update servers
// (this one included, apparently) sit behind a firewall/CDN that 403s requests missing one.
var updateClientUserAgent = $"OrbitMesh.Server-Updater/{typeof(Program).Assembly.GetName().Version}";
builder.Services.AddHttpClient(nameof(ReleaseServerClient)).ConfigureHttpClient(c => c.DefaultRequestHeaders.UserAgent.ParseAdd(updateClientUserAgent));
builder.Services.AddHttpClient(nameof(ServerSelfUpdater)).ConfigureHttpClient(c => c.DefaultRequestHeaders.UserAgent.ParseAdd(updateClientUserAgent));
builder.Services.AddHttpClient(nameof(StaticSiteUpdater)).ConfigureHttpClient(c => c.DefaultRequestHeaders.UserAgent.ParseAdd(updateClientUserAgent));

// Keep the wire format PascalCase (matching every C# model in this codebase) instead of
// System.Text.Json's camelCase default - the console's JS reads properties like
// `edge.Description.EdgeName` / `message.Key` with exact casing. Also accept/emit enums as strings,
// since the console's JS builds payloads like `{Scope:'Package', Args:[...]}` - System.Text.Json
// requires JsonStringEnumConverter explicitly for that (its default is numeric-only). OrbitMesh.Edge
// and the PackageHost SDK configure the same converter on their HubConnection for this reason.
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = null;
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.JsonSerializerOptions.Converters.Add(new OrbitMesh.Server.Services.FlexibleStringDictionaryConverter());
});
builder.Services.AddOpenApi();
builder.Services.AddSignalR().AddJsonProtocol(o =>
{
    o.PayloadSerializerOptions.PropertyNamingPolicy = null;
    o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
// AllowAnyOrigin() would let any website's JS call this API from a visitor's browser using a leaked
// AccessKey (auth here is a header/querystring value, not a cookie, so there's no separate CSRF gate).
// Restrict to explicitly configured origins instead; non-browser callers (Edge, packages, curl) are
// never subject to CORS at all, and the console is same-origin so it needs no entry here.
var allowedOrigins = builder.Configuration.GetSection($"{OrbitMeshOptions.SectionName}:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (allowedOrigins.Length > 0)
    {
        p.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader();
    }
}));

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter(OrbitMeshMetrics.MeterName)
        .AddAspNetCoreInstrumentation()
        .AddPrometheusExporter());

var listenUrls = builder.Configuration.GetSection($"{OrbitMeshOptions.SectionName}:ListenUrls").Get<string[]>();
if (listenUrls is { Length: > 0 })
{
    builder.WebHost.UseUrls(listenUrls);
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// The console builds some URLs as `{origin}/` + `/rest/...`, producing a doubled leading slash. Collapse
// it before routing ever sees the path: WebApplication auto-inserts route MATCHING at the very start of
// the pipeline when UseRouting() isn't called explicitly, so this has to run first (an explicit
// UseRouting() call right after is what makes that ordering deterministic instead of implicit).
app.Use(async (context, next) =>
{
    var value = context.Request.Path.Value;
    if (!string.IsNullOrEmpty(value) && value.Contains("//", StringComparison.Ordinal))
    {
        while (value.Contains("//", StringComparison.Ordinal))
        {
            value = value.Replace("//", "/", StringComparison.Ordinal);
        }
        context.Request.Path = new PathString(value);
    }
    await next(context);
});

app.UseRouting();

var orbitmeshOptions = app.Services.GetRequiredService<IOptionsMonitor<OrbitMeshOptions>>().CurrentValue;

// CORS must run here - before AccessKeyAuthenticationMiddleware - so that browser CORS preflight
// (OPTIONS) requests to /rest are short-circuited by the CORS middleware itself. If it ran later,
// AccessKeyAuthenticationMiddleware would see a preflight (which never carries an AccessKey) and
// reject it with 403 before CORS ever got a chance to answer it, breaking cross-origin callers even
// when their origin IS in AllowedOrigins. Skipped for any enabled FileServers path specifically:
// browsers always treat @font-face loads as CORS-mode requests, even for same-origin URLs, and a
// restrictive AllowedOrigins policy would otherwise strip Access-Control-Allow-Origin from font
// responses and silently break their icons, even though these are only ever loaded same-origin from here.
app.UseWhen(
    context => !orbitmeshOptions.FileServers.Any(fs => fs.Enable && context.Request.Path.StartsWithSegments(fs.Path, StringComparison.OrdinalIgnoreCase)),
    appBranch => appBranch.UseCors());

// A bare "/console-next" (no trailing slash) serves index.html fine, but its relative asset hrefs then
// resolve against the site root instead of "/console-next/" - redirect to add the slash.
app.Use(async (context, next) =>
{
    var requestPath = context.Request.Path;
    var matchedFileServer = orbitmeshOptions.FileServers.FirstOrDefault(fs =>
        fs.Enable && requestPath.Equals(fs.Path, StringComparison.OrdinalIgnoreCase));
    if (matchedFileServer != null)
    {
        context.Response.Redirect(requestPath.Value + "/" + context.Request.QueryString);
        return;
    }
    await next(context);
});

// AccessKeyAuthenticationMiddleware enforces each entry's LocalhostOnly for its own assets, not just
// the /rest and /signalr endpoints, so it must run before the static files below.
app.UseMiddleware<AccessKeyAuthenticationMiddleware>();

foreach (var fileServer in orbitmeshOptions.FileServers.Where(fs => fs.Enable && Directory.Exists(fs.PhysicalPath)))
{
    var fileProvider = new PhysicalFileProvider(Path.GetFullPath(fileServer.PhysicalPath));
    // DefaultFilesMiddleware looks up its DefaultFileNames candidates by exact path (not a case-insensitive
    // directory scan), so on a case-sensitive filesystem (Linux) "default.html" never matches an actual
    // "Default.html" on disk - only ever surfaced on Windows/NTFS, where a lookup is case-insensitive
    // regardless. The original console ships as "Default.html", hence the extra candidate below.
    var defaultFilesOptions = new DefaultFilesOptions { FileProvider = fileProvider, RequestPath = fileServer.Path };
    defaultFilesOptions.DefaultFileNames.Add("Default.html");
    app.UseDefaultFiles(defaultFilesOptions);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = fileProvider,
        RequestPath = fileServer.Path,
        // No far-future cache headers are set here, so without this browsers fall back to heuristic
        // caching (no explicit Cache-Control at all) and can silently keep serving an old Console
        // build after a deploy until a hard refresh. ETag/Last-Modified are still sent, so a normal
        // reload becomes a cheap 304 instead of a full re-download - this only forces the revalidation
        // to actually happen every time instead of being skipped.
        OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache"
    });
}

app.UseMiddleware<PackageFileMiddleware>();

app.MapHub<OrbitMeshHub>("/signalr/hubs/" + OrbitMeshHubNames.OrbitMesh);
app.MapHub<EdgeHub>("/signalr/hubs/" + OrbitMeshHubNames.Edge);
app.MapHub<ConsumerHub>("/signalr/hubs/" + OrbitMeshHubNames.Consumer);
app.MapHub<ControlHub>("/signalr/hubs/" + OrbitMeshHubNames.Control);

// A client-side-routed SPA (FileServerOptions.IsSpa) needs any sub-path that isn't a real file
// (e.g. /console-next/packages) to fall back to index.html so the router can resolve it - only
// reached when UseStaticFiles above already found no matching real file, and lowest-priority
// among all endpoints, so it never shadows /rest, /signalr, or another FileServers entry.
//
// MapFallbackToFile's own RequestPath handling resolves the fallback file relative to the wrong
// base when the pattern already includes that same prefix (it ends up looking for the file at the
// site root instead of under RequestPath) - reading the file directly from the FileProvider once
// at startup and serving those same bytes on every fallback hit sidesteps that entirely.
foreach (var spaFileServer in orbitmeshOptions.FileServers.Where(fs => fs.Enable && fs.IsSpa && Directory.Exists(fs.PhysicalPath)))
{
    var spaFileProvider = new PhysicalFileProvider(Path.GetFullPath(spaFileServer.PhysicalPath));
    var indexFile = spaFileProvider.GetFileInfo("index.html");
    if (!indexFile.Exists)
    {
        continue;
    }
    app.MapFallback(
        spaFileServer.Path.TrimStart('/').TrimEnd('/') + "/{*path:nonfile}",
        async context =>
        {
            context.Response.ContentType = "text/html";
            await using var stream = indexFile.CreateReadStream();
            await stream.CopyToAsync(context.Response.Body);
        });
}

app.MapControllers();
app.MapPrometheusScrapingEndpoint();

var telemetryItemManager = app.Services.GetRequiredService<OrbitMeshTelemetryItemManager>();
telemetryItemManager.Initialize();
app.Lifetime.ApplicationStopping.Register(telemetryItemManager.Close);

// One-time migration: any Machine credential still holding a plaintext AccessKey (from before
// AccessKeyCipher existed) gets encrypted in place on the next start. Human credentials are never
// touched here - they've been PBKDF2-hashed since that system was introduced and AccessKeyHasher.IsHashed
// already guards every write path, so there's nothing legacy left for them to migrate.
var cipher = app.Services.GetRequiredService<AccessKeyCipher>();
var configWriterForMigration = app.Services.GetRequiredService<IOrbitMeshConfigWriter>();
var plaintextMachineKeys = orbitmeshOptions.Credentials.Where(c => c.Kind == CredentialKind.Machine && !AccessKeyCipher.IsEncrypted(c.AccessKey)).ToList();
if (plaintextMachineKeys.Count > 0)
{
    configWriterForMigration.Update(cfg =>
    {
        foreach (var credential in cfg.Credentials.Where(c => c.Kind == CredentialKind.Machine && !AccessKeyCipher.IsEncrypted(c.AccessKey)))
        {
            credential.AccessKey = cipher.Encrypt(credential.AccessKey);
        }
    });
    app.Logger.LogInformation("Encrypted {Count} Machine credential(s) that still had a plaintext AccessKey.", plaintextMachineKeys.Count);
}

// One-time-ish migration (idempotent, re-checked every start): EdgeHub/OrbitMeshHub now require the
// EdgeConnect/PackageConnect scope just to connect (see EdgeHub.IsAuthorized/OrbitMeshHub.OnConnectedAsync),
// and OrbitMeshHub.SendMessage requires MessagesExecute - scopes that didn't exist when older credentials
// were provisioned. Without this, every already-configured Edge/Package would be locked out the moment
// this version starts, instead of only newly-provisioned ones getting the scope automatically.
static HashSet<string> CredentialNamesOf(IEnumerable<string?> names) =>
    names.Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToHashSet(StringComparer.OrdinalIgnoreCase);

var edgeCredentialNames = CredentialNamesOf(orbitmeshOptions.Edges.Select(e => e.Credential));
var packageCredentialNames = CredentialNamesOf(orbitmeshOptions.Edges.SelectMany(e => e.Packages).Select(p => p.Credential));
var needsScopeMigration = orbitmeshOptions.Credentials.Any(c =>
    (edgeCredentialNames.Contains(c.Name) && !c.Scopes.Contains(OrbitMeshScope.EdgeConnect))
    || (packageCredentialNames.Contains(c.Name) && (!c.Scopes.Contains(OrbitMeshScope.PackageConnect) || !c.Scopes.Contains(OrbitMeshScope.MessagesExecute))));
if (needsScopeMigration)
{
    configWriterForMigration.Update(cfg =>
    {
        var edgeNames = CredentialNamesOf(cfg.Edges.Select(e => e.Credential));
        var packageNames = CredentialNamesOf(cfg.Edges.SelectMany(e => e.Packages).Select(p => p.Credential));
        foreach (var credential in cfg.Credentials)
        {
            if (edgeNames.Contains(credential.Name) && !credential.Scopes.Contains(OrbitMeshScope.EdgeConnect))
            {
                credential.Scopes.Add(OrbitMeshScope.EdgeConnect);
            }
            if (packageNames.Contains(credential.Name))
            {
                if (!credential.Scopes.Contains(OrbitMeshScope.PackageConnect)) { credential.Scopes.Add(OrbitMeshScope.PackageConnect); }
                if (!credential.Scopes.Contains(OrbitMeshScope.MessagesExecute)) { credential.Scopes.Add(OrbitMeshScope.MessagesExecute); }
            }
        }
    });
    app.Logger.LogInformation("Granted EdgeConnect/PackageConnect/MessagesExecute to existing Edge/Package credentials that were missing them.");
}

// Seed the package registry from config so the console sees declared-but-not-yet-connected packages,
// and keep it in sync whenever appsettings.json changes (manual edits or ManagementController writes).
var packageRegistrySync = app.Services.GetRequiredService<PackageRegistrySync>();
packageRegistrySync.Sync();
app.Services.GetRequiredService<IOptionsMonitor<OrbitMeshOptions>>().OnChange(_ => packageRegistrySync.Sync());

// Same idea, for FileServers entries that declare an UpdateProjectSlug (Console, or any other).
var staticSiteUpdateRegistry = app.Services.GetRequiredService<StaticSiteUpdateRegistry>();
staticSiteUpdateRegistry.Sync();
app.Services.GetRequiredService<IOptionsMonitor<OrbitMeshOptions>>().OnChange(_ => staticSiteUpdateRegistry.Sync());

app.Run();
