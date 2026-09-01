using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Cronos;
using OrbitMesh;
using OrbitMesh.Deployment;
using OrbitMesh.Server.Configuration;
using OrbitMesh.Server.Hubs;
using OrbitMesh.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using NuGet.Versioning;

namespace OrbitMesh.Server.Controllers;

public sealed record InstallNuGetPackageRequest(string Feed, string Id, string Version);

public sealed record NuGetPackageProvenance(string Feed, string Id, string Version);

public sealed record NuGetPackageUpdateStatus(string PackageFile, string Feed, string Id, string InstalledVersion, string? LatestVersion, bool UpdateAvailable);

/// <summary>
/// Routes intentionally mirror <c>rest/management/{Action}</c> naming (PascalCase, no REST-y dashes) -
/// the Console's JS client (Scripts/management-api.js) calls these exact paths and payload shapes.
/// </summary>
[ApiController]
[Route("rest/management")]
public sealed class ManagementController(
    IOptionsMonitor<OrbitMeshOptions> options,
    IOrbitMeshConfigWriter configWriter,
    IOrbitMeshDirectory directory,
    IPendingEdgeRegistry pendingEdgeRegistry,
    IHubContext<EdgeHub> edgeHub,
    IPackageRegistry packageRegistry,
    UpdateStatus updateStatus,
    UpdateCheckService updateChecker,
    ServerSelfUpdater serverSelfUpdater,
    StaticSiteUpdateRegistry staticSiteRegistry,
    StaticSiteUpdateCheckService staticSiteChecker,
    StaticSiteUpdater staticSiteUpdater,
    NuGetFeedClient nuGetFeedClient,
    CredentialUsageTracker credentialUsageTracker,
    AccessKeyCipher accessKeyCipher,
    LoginAttemptLimiter loginAttemptLimiter,
    ILogger<ManagementController> logger) : ControllerBase
{
    [HttpGet("CheckAccess")]
    public IActionResult CheckAccess() => Ok();

    [HttpGet("server/version")]
    public ActionResult<string> GetServerVersion() => ServerVersion.Current;

    [HttpGet("update/status")]
    [RequiresScope(OrbitMeshScope.UpdatesManage)]
    public ActionResult<UpdateStatus> GetUpdateStatus() => updateStatus;

    [HttpPost("update/check")]
    [RequiresScope(OrbitMeshScope.UpdatesManage)]
    public async Task<ActionResult<UpdateStatus>> CheckForUpdate()
    {
        await updateChecker.CheckNowAsync();
        return updateStatus;
    }

    [HttpPost("update/apply")]
    [RequiresScope(OrbitMeshScope.UpdatesManage)]
    public async Task<IActionResult> ApplyUpdate()
    {
        if (string.IsNullOrEmpty(updateStatus.ZipUrl))
        {
            return BadRequest("No update currently available.");
        }
        try
        {
            await serverSelfUpdater.ApplyAsync(updateStatus.ZipUrl);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
        // ApplyAsync already handed off to OrbitMesh.Updater and scheduled this process's own exit - it
        // will swap the staged files into place and restart the server once this process is gone.
        return Ok("Update applied - OrbitMesh.Updater will restart the server shortly.");
    }

    [HttpGet("update/sites")]
    [RequiresScope(OrbitMeshScope.UpdatesManage)]
    public ActionResult<List<StaticSiteUpdateStatus>> GetStaticSiteUpdates() => staticSiteRegistry.All().ToList();

    [HttpPost("update/sites/check")]
    [RequiresScope(OrbitMeshScope.UpdatesManage)]
    public async Task<ActionResult<List<StaticSiteUpdateStatus>>> CheckStaticSiteUpdates()
    {
        await staticSiteChecker.CheckAllAsync();
        return staticSiteRegistry.All().ToList();
    }

    [HttpPost("update/sites/{slug}/apply")]
    [RequiresScope(OrbitMeshScope.UpdatesManage)]
    public async Task<IActionResult> ApplyStaticSiteUpdate(string slug)
    {
        var status = staticSiteRegistry.Find(slug);
        var fileServer = staticSiteRegistry.FindFileServer(slug);
        if (status == null || fileServer == null)
        {
            return NotFound($"No update-managed site with slug '{slug}'.");
        }
        if (string.IsNullOrEmpty(status.ZipUrl) || string.IsNullOrEmpty(status.LatestVersion))
        {
            return BadRequest("No update currently available for this site.");
        }
        try
        {
            await staticSiteUpdater.ApplyAsync(fileServer, status.LatestVersion, status.ZipUrl);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
        }
        // No process restart needed here (unlike ApplyUpdate above) - the files just swapped in are
        // what Kestrel's static file middleware reads on the very next request.
        await staticSiteChecker.CheckOneAsync(status);
        return Ok($"Update applied to '{fileServer.Path}'.");
    }

    [HttpGet("configuration")]
    [RequiresScope(OrbitMeshScope.ConfigurationRead)]
    public ContentResult GetServerConfiguration() => Content(configWriter.ReadRawJson(), "application/json");

    [HttpPost("configuration")]
    [RequiresScope(OrbitMeshScope.ConfigurationWrite)]
    public IActionResult SetServerConfiguration([FromBody] object rawJson)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(rawJson);

        OrbitMeshOptions candidate;
        try
        {
            candidate = configWriter.Deserialize(json);
        }
        catch (Exception ex)
        {
            return BadRequest($"Configuration is not valid - refusing to save: {ex.Message}");
        }

        // The one check that actually matters: whoever is saving this must still be able to manage
        // the server afterwards. Catches the obvious slip (typo/deleted AccessKey, cleared scopes, a
        // removed credential) before it's written - the alternative is discovering it only after every
        // Management API call, including the one needed to fix it, starts returning 403. Identity is
        // resolved from the CURRENT config (whoever is really calling right now), then checked against
        // the CANDIDATE config being saved (would that same credential still have configuration:write
        // afterwards?) - a raw AccessKey string compare doesn't work here since Human/Machine keys are
        // hashed/encrypted at rest, never equal to the raw presented value.
        var callerAccessKey = Request.GetHeaderOrQuery(OrbitMeshHeaderNames.AccessKey);
        var callerName = directory.GetCredentialName(callerAccessKey);
        var callerStillHasAccess = callerName != null && candidate.Credentials.Any(c =>
            c.Enable && c.Name.Equals(callerName, StringComparison.OrdinalIgnoreCase) && c.Scopes.Contains(OrbitMeshScope.ConfigurationWrite));
        if (!callerStillHasAccess)
        {
            return BadRequest(
                "This change would revoke your own configuration:write access (no enabled credential named " +
                $"'{callerName}' would keep that scope) - refusing to apply it.");
        }

        configWriter.WriteRawJson(json);
        return Ok();
    }

    [HttpGet("configuration/reload")]
    [RequiresScope(OrbitMeshScope.ConfigurationWrite)]
    public IActionResult ReloadServerConfiguration(bool deployConfiguration = false) =>
        // Configuration hot-reloads automatically (appsettings.json watched by IOptionsMonitor);
        // deployment of settings to already-connected packages happens via ControlHub.ReloadServerConfiguration.
        Ok();

    [HttpGet("configuration/recoveryoptions")]
    [RequiresScope(OrbitMeshScope.ConfigurationRead)]
    public ActionResult<RecoveryOptions> GetGlobalRecoveryOptions() => options.CurrentValue.RecoveryOptions;

    [HttpPost("configuration/recoveryoptions")]
    [RequiresScope(OrbitMeshScope.ConfigurationWrite)]
    public IActionResult SetGlobalRecoveryOptions([FromBody] RecoveryOptions recoveryOptions)
    {
        configWriter.Update(cfg => cfg.RecoveryOptions = recoveryOptions);
        return Ok();
    }

    [HttpGet("edges")]
    [RequiresScope(OrbitMeshScope.EdgesRead)]
    public ActionResult<List<object>> GetEdges() =>
        options.CurrentValue.Edges.Select(s => new { s.Name, s.Credential }).Cast<object>().ToList();

    [HttpPost("edges")]
    [RequiresScope(OrbitMeshScope.EdgesWrite)]
    public IActionResult InsertOrUpdateEdge([FromBody] EdgeOptions edge)
    {
        if (!options.CurrentValue.Credentials.Any(c => c.Name.Equals(edge.Credential, StringComparison.OrdinalIgnoreCase)))
        {
            return NotFound($"The credential '{edge.Credential}' does not exist");
        }
        configWriter.Update(cfg =>
        {
            var existing = cfg.Edges.FirstOrDefault(s => s.Name.Equals(edge.Name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Credential = edge.Credential;
            }
            else
            {
                cfg.Edges.Add(new EdgeOptions { Name = edge.Name, Credential = edge.Credential });
            }
        });
        return Ok();
    }

    [HttpDelete("edges/{edge}")]
    [RequiresScope(OrbitMeshScope.EdgesWrite)]
    public IActionResult RemoveEdge(string edge)
    {
        if (!directory.EdgeExists(edge))
        {
            return NotFound();
        }
        configWriter.Update(cfg => cfg.Edges.RemoveAll(s => s.Name.Equals(edge, StringComparison.OrdinalIgnoreCase)));
        return Ok();
    }

    [HttpGet("edges/pending")]
    [RequiresScope(OrbitMeshScope.EdgesRead)]
    public ActionResult<List<PendingEdgeInfo>> GetPendingEdges() => pendingEdgeRegistry.GetAll();

    public sealed record ApprovePendingEdgeRequest(string Name);

    // CredentialsManage, not EdgesWrite: this mints a new credential too.
    [HttpPost("edges/pending/{instanceId}/approve")]
    [RequiresScope(OrbitMeshScope.CredentialsManage)]
    public async Task<ActionResult<object>> ApprovePendingEdge(string instanceId, [FromBody] ApprovePendingEdgeRequest request)
    {
        var pending = pendingEdgeRegistry.Get(instanceId);
        if (pending == null)
        {
            return NotFound("No pending Edge with this InstanceId - it may have already been approved or given up retrying.");
        }
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }
        if (options.CurrentValue.Edges.Any(e => e.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase))
            || options.CurrentValue.Credentials.Any(c => c.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return Conflict($"'{request.Name}' is already in use by an existing edge or credential.");
        }

        var accessKey = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        configWriter.Update(cfg =>
        {
            cfg.Credentials.Add(new CredentialOptions
            {
                Name = request.Name,
                AccessKey = accessKeyCipher.Encrypt(accessKey),
                Kind = CredentialKind.Machine,
                Enable = true,
                Scopes = [OrbitMeshScope.EdgeConnect]
            });
            cfg.Edges.Add(new EdgeOptions { Name = request.Name, Credential = request.Name, InstanceId = instanceId });
        });
        pendingEdgeRegistry.Remove(instanceId);

        var pushed = false;
        try
        {
            await edgeHub.Clients.Client(pending.ConnectionId).SendAsync(EdgeClientMethodNames.EdgeApproved, accessKey);
            pushed = true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to push the new credential to Edge '{Name}' (connectionId='{ConnectionId}') - it will need the AccessKey applied by hand.", request.Name.ForLog(), pending.ConnectionId);
        }
        return Ok(new { name = request.Name, accessKey, pushed });
    }

    [HttpDelete("edges/pending/{instanceId}")]
    [RequiresScope(OrbitMeshScope.EdgesWrite)]
    public IActionResult DismissPendingEdge(string instanceId)
    {
        pendingEdgeRegistry.Remove(instanceId);
        return Ok();
    }

    // LoginAttemptLimiter's brute-force lockout is in-memory and IP-keyed - useful in dev (a flaky
    // client hammering the wrong AccessKey trips it fast) with no way to clear one entry short of
    // restarting the whole server. Read-only visibility gets ConfigurationRead; clearing one gets
    // ConfigurationWrite, same split as the rest of the server's own operational settings.
    [HttpGet("security/lockouts")]
    [RequiresScope(OrbitMeshScope.ConfigurationRead)]
    public ActionResult<IReadOnlyList<LockedOutIp>> GetLockedOutIps() => Ok(loginAttemptLimiter.GetLockedOutIps());

    [HttpDelete("security/lockouts/{ip}")]
    [RequiresScope(OrbitMeshScope.ConfigurationWrite)]
    public IActionResult UnlockIp(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address))
        {
            return BadRequest("Not a valid IP address.");
        }
        return loginAttemptLimiter.Unlock(address) ? Ok() : NotFound();
    }

    private static EdgeOptions? FindEdge(OrbitMeshOptions cfg, string edgeName) =>
        cfg.Edges.FirstOrDefault(s => s.Name.Equals(edgeName, StringComparison.OrdinalIgnoreCase));

    private static PackageOptions? FindPackage(EdgeOptions? edge, string packageName) =>
        edge?.Packages.FirstOrDefault(p => p.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase));

    [HttpGet("edges/{edge}/packages")]
    [RequiresScope(OrbitMeshScope.PackagesRead)]
    public ActionResult<List<PackageOptions>> GetPackages(string edge)
    {
        var s = FindEdge(options.CurrentValue, edge);
        return s == null ? NotFound() : s.Packages;
    }

    [HttpGet("edges/{edge}/packages/{package}")]
    [RequiresScope(OrbitMeshScope.PackagesRead)]
    public ActionResult<PackageOptions> GetPackage(string edge, string package)
    {
        var p = FindPackage(FindEdge(options.CurrentValue, edge), package);
        return p == null ? NotFound() : p;
    }

    // Package.Name ends up as a directory name (OrbitMesh.Edge's InstanceDirectory) and as a
    // "dotnet <path>" process argument on the Edge side, which never re-validates it (it trusts the
    // server). Rejecting path-traversal/separator characters here, at the point the name is first
    // accepted, stops a malicious or buggy Management API caller before it reaches the Edge.
    private static bool IsValidPackageName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && !name.Contains('/', StringComparison.Ordinal)
        && !name.Contains('\\', StringComparison.Ordinal)
        && !name.Contains("..", StringComparison.Ordinal)
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    [HttpPost("edges/{edge}/packages")]
    [RequiresScope(OrbitMeshScope.PackagesManage)]
    public IActionResult InsertOrUpdatePackage(string edge, [FromBody] PackageOptions package)
    {
        if (!directory.EdgeExists(edge))
        {
            return NotFound($"The edge '{edge}' does not exist");
        }
        if (!IsValidPackageName(package.Name))
        {
            return BadRequest($"Invalid package name '{package.Name}'");
        }
        configWriter.Update(cfg =>
        {
            var s = FindEdge(cfg, edge)!;
            var existing = FindPackage(s, package.Name);
            if (existing != null)
            {
                package.RecoveryOptions ??= existing.RecoveryOptions;
                package.Groups = existing.Groups;
                package.Settings = existing.Settings;
                package.Credential = existing.Credential;
                s.Packages.Remove(existing);
            }
            if (string.IsNullOrEmpty(package.Credential))
            {
                package.Credential = EnsurePackageCredential(cfg, edge, package.Name, package.Groups);
            }
            s.Packages.Add(package);
        });
        return Ok();
    }

    // Gives the package its own credential instead of falling back to the Edge's own broader one.
    private string EnsurePackageCredential(OrbitMeshOptions cfg, string edgeName, string packageName, List<string> groups)
    {
        var credentialName = $"{edgeName}.{packageName}";
        var credential = cfg.Credentials.FirstOrDefault(c => c.Name.Equals(credentialName, StringComparison.OrdinalIgnoreCase));
        if (credential == null)
        {
            credential = new CredentialOptions
            {
                Name = credentialName,
                AccessKey = accessKeyCipher.Encrypt(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))),
                Kind = CredentialKind.Machine,
                Enable = true
            };
            cfg.Credentials.Add(credential);
        }
        SyncPackageCredentialAuthorizations(credential, edgeName, packageName, groups);
        return credentialName;
    }

    // Rebuilds the rule sets from scratch - safe to call on provisioning and on every Groups change.
    private static void SyncPackageCredentialAuthorizations(CredentialOptions credential, string edgeName, string packageName, List<string> groups)
    {
        credential.Authorizations.TelemetryItems = new AuthorizationRuleSet<TelemetryItemAuthorizationRule>
        {
            DefaultAuthorization = AuthorizationType.Deny,
            Rules = [new TelemetryItemAuthorizationRule { EdgeName = edgeName, PackageName = packageName, Name = "*", Authorization = AuthorizationType.Allow }]
        };
        credential.Authorizations.Groups = new AuthorizationRuleSet<GroupAuthorizationRule>
        {
            DefaultAuthorization = AuthorizationType.Deny,
            Rules = groups.Select(g => new GroupAuthorizationRule { GroupName = g, Authorization = AuthorizationType.Allow }).ToList()
        };
        credential.Authorizations.Messages = new AuthorizationRuleSet<MessageAuthorizationRule>
        {
            DefaultAuthorization = AuthorizationType.Deny,
            // Allowed to send to itself (Package/Edge scope targeting its own names) and to its own
            // configured Groups - NOT to All/Others/another package's name, which needs an explicit Rule.
            Rules =
            [
                new MessageAuthorizationRule { Scope = MessageScope.ScopeType.Package, Args = packageName, Authorization = AuthorizationType.Allow },
                new MessageAuthorizationRule { Scope = MessageScope.ScopeType.Edge, Args = edgeName, Authorization = AuthorizationType.Allow },
                .. groups.Select(g => new MessageAuthorizationRule { Scope = MessageScope.ScopeType.Group, Args = g, Authorization = AuthorizationType.Allow })
            ]
        };

        // Additive, unlike Authorizations above - never removes a scope an admin granted by hand, but
        // makes sure every package credential (including ones provisioned before PackageConnect/
        // MessagesExecute existed) has what OrbitMeshHub now requires just to connect and send messages.
        EnsureScope(credential, OrbitMeshScope.PackageConnect);
        EnsureScope(credential, OrbitMeshScope.MessagesExecute);
    }

    private static void EnsureScope(CredentialOptions credential, string scope)
    {
        if (!credential.Scopes.Contains(scope))
        {
            credential.Scopes.Add(scope);
        }
    }

    [HttpDelete("edges/{edge}/packages/{package}")]
    [RequiresScope(OrbitMeshScope.PackagesManage)]
    public IActionResult RemovePackage(string edge, string package)
    {
        if (!directory.EdgeExists(edge))
        {
            return NotFound();
        }
        configWriter.Update(cfg => FindEdge(cfg, edge)!.Packages.RemoveAll(p => p.Name.Equals(package, StringComparison.OrdinalIgnoreCase)));
        // PackageRegistrySync.Sync() (triggered by the config write above, via IOptionsMonitor.OnChange)
        // only purges a registry entry once it's BOTH no longer configured AND no longer connected - at
        // the instant this write lands the Edge hasn't been told to stop the package yet, so it's still
        // connected and Sync() leaves it in place. Nothing re-triggers Sync() afterward once the Edge
        // does disconnect, so without this the removed package would linger forever as a disconnected
        // "ghost" row in the console. Removing it here immediately is correct either way - the admin
        // explicitly asked for this package to be gone, regardless of whether the Edge has caught up yet.
        packageRegistry.Remove(directory.GetPackageInstanceId(edge, package));
        return Ok();
    }

    [HttpGet("edges/{edge}/packages/{package}/recoveryoptions")]
    [RequiresScope(OrbitMeshScope.PackagesRead)]
    public ActionResult<RecoveryOptions> GetPackageRecoveryOptions(string edge, string package)
    {
        var p = FindPackage(FindEdge(options.CurrentValue, edge), package);
        return p == null ? NotFound() : (p.RecoveryOptions ?? options.CurrentValue.RecoveryOptions);
    }

    [HttpPost("edges/{edge}/packages/{package}/recoveryoptions")]
    [RequiresScope(OrbitMeshScope.PackagesManage)]
    public IActionResult SetPackageRecoveryOptions(string edge, string package, [FromBody] RecoveryOptions recoveryOptions)
    {
        if (!directory.EdgeExists(edge))
        {
            return NotFound();
        }
        configWriter.Update(cfg =>
        {
            var p = FindPackage(FindEdge(cfg, edge), package);
            if (p != null)
            {
                p.RecoveryOptions = recoveryOptions;
            }
        });
        return Ok();
    }

    [HttpGet("edges/{edge}/packages/{package}/groups")]
    [RequiresScope(OrbitMeshScope.PackagesRead)]
    public ActionResult<List<string>> GetPackageGroups(string edge, string package) => directory.GetGroups(edge, package);

    [HttpPost("edges/{edge}/packages/{package}/groups")]
    [RequiresScope(OrbitMeshScope.PackagesManage)]
    public IActionResult SetPackageGroups(string edge, string package, [FromBody] List<string> groups)
    {
        if (!directory.EdgeExists(edge))
        {
            return NotFound();
        }
        configWriter.Update(cfg =>
        {
            var p = FindPackage(FindEdge(cfg, edge), package);
            if (p != null)
            {
                p.Groups = groups;
                var credential = cfg.Credentials.FirstOrDefault(c => c.Name.Equals(p.Credential, StringComparison.OrdinalIgnoreCase));
                if (credential != null)
                {
                    SyncPackageCredentialAuthorizations(credential, edge, package, groups);
                }
            }
        });
        return Ok();
    }

    [HttpGet("edges/{edge}/packages/{package}/settings")]
    [RequiresScope(OrbitMeshScope.PackagesRead)]
    public ActionResult<Dictionary<string, string>> GetPackageSettings(string edge, string package) =>
        directory.GetSettings(edge, package);

    public sealed class SetPackageSettingsRequest
    {
        public Dictionary<string, string> Settings { get; set; } = new();
        public bool RefreshPackage { get; set; }
    }

    [HttpPost("edges/{edge}/packages/{package}/settings")]
    [RequiresScope(OrbitMeshScope.PackagesManage)]
    public IActionResult SetPackageSettings(string edge, string package, [FromBody] SetPackageSettingsRequest request)
    {
        var p = FindPackage(FindEdge(options.CurrentValue, edge), package);
        if (p == null)
        {
            return NotFound();
        }
        var validationError = ValidateJsonAndXmlSettings(p, request.Settings);
        if (validationError != null)
        {
            return BadRequest(validationError);
        }
        configWriter.Update(cfg =>
        {
            var writable = FindPackage(FindEdge(cfg, edge), package);
            if (writable != null)
            {
                writable.Settings = request.Settings;
            }
        });
        return Ok();
    }

    private static readonly System.Text.RegularExpressions.Regex SettingVariableTokenPattern = new(@"\{(\w+)\}", System.Text.RegularExpressions.RegexOptions.Compiled);

    // "0" stands in for a "{Variable}" token, valid JSON whether it sits bare or inside quotes.
    private string? ValidateJsonAndXmlSettings(PackageOptions package, Dictionary<string, string> settings)
    {
        if (!TryResolveZipPath(package.PackageFile ?? package.Name, out var zipPath))
        {
            return null;
        }
        var manifest = System.IO.File.Exists(zipPath) ? PackageZipHelper.GetManifest(zipPath) : null;
        if (manifest == null)
        {
            return null;
        }
        foreach (var (name, value) in settings)
        {
            var setting = manifest.Settings.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (setting == null || string.IsNullOrEmpty(value))
            {
                continue;
            }
            var placeholder = SettingVariableTokenPattern.Replace(value, "0");
            if (setting.Type == OrbitMesh.Deployment.SettingType.JsonObject)
            {
                try
                {
                    System.Text.Json.JsonDocument.Parse(placeholder).Dispose();
                }
                catch (System.Text.Json.JsonException ex)
                {
                    return $"Invalid JSON in \"{name}\": {ex.Message}";
                }
            }
            else if (setting.Type == OrbitMesh.Deployment.SettingType.XmlDocument)
            {
                try
                {
                    new System.Xml.XmlDocument().LoadXml(placeholder);
                }
                catch (System.Xml.XmlException ex)
                {
                    return $"Invalid XML in \"{name}\": {ex.Message}";
                }
            }
        }
        return null;
    }

    public sealed record VariableSummary(string Name, bool IsSecret, string? Value);

    // A secret's value is never sent here (Value is null) - RevealVariable decrypts it on demand.
    [HttpGet("variables")]
    [RequiresScope(OrbitMeshScope.PackagesRead)]
    public ActionResult<List<VariableSummary>> GetVariables() =>
        options.CurrentValue.Variables.Select(v => new VariableSummary(v.Name, v.IsSecret, v.IsSecret ? null : v.Value)).ToList();

    [HttpGet("variables/{name}/reveal")]
    [RequiresScope(OrbitMeshScope.PackagesManage)]
    public ActionResult<string> RevealVariable(string name)
    {
        var variable = options.CurrentValue.Variables.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (variable == null)
        {
            return NotFound();
        }
        if (!variable.IsSecret)
        {
            return variable.Value;
        }
        try
        {
            return accessKeyCipher.Decrypt(variable.Value);
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException)
        {
            return UnprocessableEntity("This Variable can't be decrypted - its stored value doesn't match the server's current encryption key.");
        }
    }

    public sealed record UpsertVariableRequest(string Name, string Value, bool IsSecret);

    [HttpPost("variables")]
    [RequiresScope(OrbitMeshScope.PackagesManage)]
    public IActionResult InsertOrUpdateVariable([FromBody] UpsertVariableRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }
        configWriter.Update(cfg =>
        {
            var existing = cfg.Variables.FirstOrDefault(v => v.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase));
            // Blank Value on an existing secret means "leave it alone" (same convention as Credentials).
            var value = request.IsSecret
                ? (string.IsNullOrEmpty(request.Value) && existing is { IsSecret: true } ? existing.Value : accessKeyCipher.Encrypt(request.Value))
                : request.Value;
            if (existing != null)
            {
                cfg.Variables.Remove(existing);
            }
            cfg.Variables.Add(new VariableOptions { Name = request.Name, Value = value, IsSecret = request.IsSecret });
        });
        return Ok();
    }

    [HttpDelete("variables/{name}")]
    [RequiresScope(OrbitMeshScope.PackagesManage)]
    public IActionResult RemoveVariable(string name)
    {
        configWriter.Update(cfg => cfg.Variables.RemoveAll(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        return Ok();
    }

    [HttpGet("scheduledtasks")]
    [RequiresScope(OrbitMeshScope.SchedulesRead)]
    public ActionResult<List<ScheduledTaskOptions>> GetScheduledTasks() => options.CurrentValue.ScheduledTasks;

    [HttpPost("scheduledtasks")]
    [RequiresScope(OrbitMeshScope.SchedulesManage)]
    public IActionResult InsertOrUpdateScheduledTask([FromBody] ScheduledTaskOptions task)
    {
        if (string.IsNullOrWhiteSpace(task.Name))
        {
            return BadRequest("Name is required.");
        }
        try
        {
            CronExpression.Parse(task.CronExpression);
        }
        catch (Exception ex)
        {
            return BadRequest($"Invalid cron expression: {ex.Message}");
        }
        configWriter.Update(cfg =>
        {
            var existing = cfg.ScheduledTasks.FirstOrDefault(t => t.Name.Equals(task.Name, StringComparison.OrdinalIgnoreCase));
            // A brand new task starts counting occurrences from whenever ScheduledTaskRunner next
            // notices it (see its own doc comment) rather than replaying history - LastRunUtc only
            // ever comes from that service, never from the Console.
            task.LastRunUtc = existing?.LastRunUtc;
            if (existing != null)
            {
                cfg.ScheduledTasks.Remove(existing);
            }
            cfg.ScheduledTasks.Add(task);
        });
        return Ok();
    }

    [HttpDelete("scheduledtasks/{name}")]
    [RequiresScope(OrbitMeshScope.SchedulesManage)]
    public IActionResult RemoveScheduledTask(string name)
    {
        configWriter.Update(cfg => cfg.ScheduledTasks.RemoveAll(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        return Ok();
    }

    [HttpGet("credentials")]
    [RequiresScope(OrbitMeshScope.CredentialsRead)]
    public ActionResult<List<object>> GetCredentials(bool includeAccessKeys = false) =>
        options.CurrentValue.Credentials.Select(c => new
        {
            c.Name,
            // Neither kind ever sends a real key back, regardless of includeAccessKeys: Human stores a
            // one-way hash (nothing to send), and Machine is encrypted specifically so the Console can
            // treat it the same way - shown once at creation/reset, never again (PAT-style). Role/enable
            // edits go through this same endpoint with AccessKey left blank (see below).
            c.Kind,
            c.Enable,
            c.Scopes,
            LastUsedUtc = credentialUsageTracker.GetLastUsed(c.Name) is { } live && (c.LastUsedUtc == null || live > c.LastUsedUtc)
                ? live
                : c.LastUsedUtc
        }).Cast<object>().ToList();

    private string StoreAccessKey(CredentialOptions credential) => credential.Kind switch
    {
        CredentialKind.Human when !AccessKeyHasher.IsHashed(credential.AccessKey) => AccessKeyHasher.Hash(credential.AccessKey),
        CredentialKind.Machine when !AccessKeyCipher.IsEncrypted(credential.AccessKey) => accessKeyCipher.Encrypt(credential.AccessKey),
        _ => credential.AccessKey
    };

    [HttpPost("credentials")]
    [RequiresScope(OrbitMeshScope.CredentialsManage)]
    public IActionResult InsertOrUpdateCredential([FromBody] CredentialOptions credential)
    {
        configWriter.Update(cfg =>
        {
            var existing = cfg.Credentials.FirstOrDefault(c => c.Name.Equals(credential.Name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                credential.Authorizations = existing.Authorizations;
                credential.LastUsedUtc = existing.LastUsedUtc;
                // A blank AccessKey means "editing roles/flags only, leave the key alone" - the console
                // never has a real key to resubmit for either kind (see GetCredentials above), and this
                // also means re-saving a credential's other fields can't accidentally wipe its key.
                credential.AccessKey = string.IsNullOrEmpty(credential.AccessKey) ? existing.AccessKey : StoreAccessKey(credential);
                cfg.Credentials.Remove(existing);
            }
            else
            {
                credential.AccessKey = StoreAccessKey(credential);
            }
            cfg.Credentials.Add(credential);
        });
        return Ok();
    }

    public sealed record SetupStatus(bool NeedsSetup);

    public sealed record CreateAdminRequest(string Name, string AccessKey);

    // Unauthenticated by design (see AccessKeyAuthenticationMiddleware's bypass list) - there is no
    // credential yet to authenticate with on a fresh install. NeedsSetup flips to false the moment the
    // first credential exists, so this pair of endpoints is only ever usable once per install.
    [HttpGet("setup/status")]
    public ActionResult<SetupStatus> GetSetupStatus() => new SetupStatus(options.CurrentValue.Credentials.Count == 0);

    [HttpPost("setup/create-admin")]
    public IActionResult CreateAdmin([FromBody] CreateAdminRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.AccessKey))
        {
            return BadRequest();
        }

        var created = false;
        configWriter.Update(cfg =>
        {
            // Re-check inside the writer lock, not just at the top of the action: two concurrent
            // first-run requests must not both succeed and create two "sole admin" accounts.
            if (cfg.Credentials.Count > 0)
            {
                return;
            }
            cfg.Credentials.Add(new CredentialOptions
            {
                Name = request.Name,
                AccessKey = AccessKeyHasher.Hash(request.AccessKey),
                Kind = CredentialKind.Human,
                Enable = true,
                Scopes = OrbitMeshScope.All.ToList()
            });
            created = true;
        });

        return created ? Ok() : Conflict();
    }

    [HttpDelete("credentials/{credential}")]
    [RequiresScope(OrbitMeshScope.CredentialsManage)]
    public IActionResult RemoveCredential(string credential)
    {
        var inUse = options.CurrentValue.Edges.Any(s =>
            s.Credential?.Equals(credential, StringComparison.OrdinalIgnoreCase) == true
            || s.Packages.Any(p => p.Credential?.Equals(credential, StringComparison.OrdinalIgnoreCase) == true));
        if (inUse)
        {
            return Conflict($"The credential '{credential}' cannot be removed because a edge or package uses it.");
        }
        configWriter.Update(cfg => cfg.Credentials.RemoveAll(c => c.Name.Equals(credential, StringComparison.OrdinalIgnoreCase)));
        return Ok();
    }

    [HttpGet("credentials/{credential}/authorizations")]
    [RequiresScope(OrbitMeshScope.CredentialsRead)]
    public ActionResult<AuthorizationOptions> GetCredentialAuthorizations(string credential)
    {
        var c = options.CurrentValue.Credentials.FirstOrDefault(x => x.Name.Equals(credential, StringComparison.OrdinalIgnoreCase));
        return c == null ? NotFound() : c.Authorizations;
    }

    [HttpPost("credentials/{credential}/authorizations")]
    [RequiresScope(OrbitMeshScope.CredentialsManage)]
    public IActionResult SetCredentialAuthorizations(string credential, [FromBody] AuthorizationOptions authorizations)
    {
        if (!options.CurrentValue.Credentials.Any(c => c.Name.Equals(credential, StringComparison.OrdinalIgnoreCase)))
        {
            return NotFound();
        }
        configWriter.Update(cfg =>
        {
            var c = cfg.Credentials.First(x => x.Name.Equals(credential, StringComparison.OrdinalIgnoreCase));
            c.Authorizations = authorizations;
        });
        return Ok();
    }

    [HttpGet("packages")]
    [RequiresScope(OrbitMeshScope.PackagesDeploy)]
    public ActionResult<List<object>> GetPackageRepository()
    {
        var dir = Path.GetFullPath(options.CurrentValue.PackagesRootDirectory);
        if (!Directory.Exists(dir))
        {
            return new List<object>();
        }
        return Directory.GetFiles(dir, "*.zip").Select(BuildPackageRepositoryEntry).Cast<object>().ToList();
    }

    [HttpGet("packages/{package}")]
    [RequiresScope(OrbitMeshScope.PackagesDeploy)]
    public ActionResult<object> GetPackage(string package)
    {
        if (!TryResolveZipPath(package, out var path))
        {
            return NotFound();
        }
        return System.IO.File.Exists(path) ? BuildPackageRepositoryEntry(path) : NotFound();
    }

    // First-cut verification surface for the NuGet feed client - not yet wired into the console's
    // Package Repository UI, just enough to prove the client works against a real feed end to end.
    [HttpGet("packages/nuget/search")]
    [RequiresScope(OrbitMeshScope.PackagesDeploy)]
    public async Task<ActionResult<IReadOnlyList<NuGetFeedPackageSummary>>> SearchNuGetFeeds([FromQuery] string q = "")
    {
        return (await nuGetFeedClient.SearchAsync(q)).ToList();
    }

    [HttpGet("packages/nuget/{feed}/{id}/versions")]
    [RequiresScope(OrbitMeshScope.PackagesDeploy)]
    public async Task<ActionResult<IReadOnlyList<string>>> GetNuGetPackageVersions(string feed, string id)
    {
        var versions = await nuGetFeedClient.GetVersionsAsync(feed, id);
        return versions.Select(v => v.ToNormalizedString()).ToList();
    }

    // Downloads a package's .nupkg, unpacks its "content/" folder (see build-packages.ps1) into the
    // local repository as a normal flat .zip - so once installed, it's indistinguishable from a
    // manually-uploaded one to the rest of the app (Edge deploy, settings editor, ...). A sidecar
    // .nuget.json records where it came from, purely so CheckNuGetUpdates below has something to
    // compare against later - nothing else reads it.
    [HttpPost("packages/nuget/install")]
    [RequiresScope(OrbitMeshScope.PackagesDeploy)]
    public async Task<ActionResult<object>> InstallFromNuGet([FromBody] InstallNuGetPackageRequest request)
    {
        var version = NuGetVersion.Parse(request.Version);
        var tempNupkg = Path.GetTempFileName();
        try
        {
            await using (var fileStream = System.IO.File.Create(tempNupkg))
            {
                var found = await nuGetFeedClient.DownloadToStreamAsync(request.Feed, request.Id, version, fileStream);
                if (!found)
                {
                    return NotFound($"'{request.Id}' {request.Version} was not found on feed '{request.Feed}'.");
                }
            }

            var tempContentDir = Path.Combine(Path.GetTempPath(), $"orbitmesh-nuget-install-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempContentDir);
            try
            {
                using (var archive = System.IO.Compression.ZipFile.OpenRead(tempNupkg))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (!entry.FullName.StartsWith("content/", StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith('/'))
                        {
                            continue;
                        }
                        var relative = entry.FullName["content/".Length..];
                        // ExtractToFile has no zip-slip protection of its own (unlike ZipFile.ExtractToDirectory,
                        // which we can't use here since only "content/" entries are wanted) - a crafted entry
                        // like "content/../../../foo" would otherwise write outside tempContentDir.
                        var destination = Path.GetFullPath(Path.Combine(tempContentDir, relative));
                        if (!IsWithin(tempContentDir, destination))
                        {
                            return BadRequest($"'{request.Id}' {request.Version} contains an unsafe archive entry ('{entry.FullName}') and was not installed.");
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        entry.ExtractToFile(destination, overwrite: true);
                    }
                }

                var manifest = OrbitMesh.Deployment.PackageManifestHelper.GetPackageManifestFromDirectory(tempContentDir, throwException: false);
                var packageName = manifest?.Name ?? request.Id;
                if (!TryResolveZipPath(packageName, out var zipPath))
                {
                    return BadRequest($"'{packageName}' is not a valid package name.");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
                if (System.IO.File.Exists(zipPath))
                {
                    System.IO.File.Delete(zipPath);
                }
                System.IO.Compression.ZipFile.CreateFromDirectory(tempContentDir, zipPath);

                var sidecar = new NuGetPackageProvenance(request.Feed, request.Id, version.ToNormalizedString());
                await System.IO.File.WriteAllTextAsync(NuGetSidecarPath(zipPath), System.Text.Json.JsonSerializer.Serialize(sidecar));

                return Ok(BuildPackageRepositoryEntry(zipPath));
            }
            finally
            {
                Directory.Delete(tempContentDir, recursive: true);
            }
        }
        finally
        {
            System.IO.File.Delete(tempNupkg);
        }
    }

    // For every locally-installed package that was installed from a feed (has a provenance sidecar),
    // checks whether a newer version now exists there. Mirrors UpdateCheckService's own shape
    // (current/latest/available) so the console can reuse the same "update available" UI pattern it
    // already has for the Server/Console self-update - this is just on-demand instead of a background
    // poll, and manually applying it (re-installing) is the equivalent of that flow's "Apply" button.
    [HttpGet("packages/nuget/check-updates")]
    [RequiresScope(OrbitMeshScope.PackagesDeploy)]
    public async Task<ActionResult<List<NuGetPackageUpdateStatus>>> CheckNuGetUpdates()
    {
        var dir = Path.GetFullPath(options.CurrentValue.PackagesRootDirectory);
        if (!Directory.Exists(dir))
        {
            return new List<NuGetPackageUpdateStatus>();
        }
        var results = new List<NuGetPackageUpdateStatus>();
        foreach (var zipPath in Directory.GetFiles(dir, "*.zip"))
        {
            var sidecarPath = NuGetSidecarPath(zipPath);
            if (!System.IO.File.Exists(sidecarPath))
            {
                continue;
            }
            var provenance = System.Text.Json.JsonSerializer.Deserialize<NuGetPackageProvenance>(await System.IO.File.ReadAllTextAsync(sidecarPath));
            if (provenance == null)
            {
                continue;
            }
            var installed = NuGetVersion.Parse(provenance.Version);
            try
            {
                var versions = await nuGetFeedClient.GetVersionsAsync(provenance.Feed, provenance.Id);
                var latest = versions.OrderByDescending(v => v).FirstOrDefault();
                results.Add(new NuGetPackageUpdateStatus(
                    Path.GetFileNameWithoutExtension(zipPath), provenance.Feed, provenance.Id,
                    installed.ToNormalizedString(), latest?.ToNormalizedString(),
                    latest != null && latest > installed));
            }
            catch (Exception ex)
            {
                // A single stale/renamed feed (e.g. a provenance sidecar left over from before a feed
                // was renamed) shouldn't 500 the whole batch and blank every package's status.
                logger.LogWarning(ex, "Unable to check updates for '{PackageFile}' via feed '{Feed}'", Path.GetFileNameWithoutExtension(zipPath), provenance.Feed);
            }
        }
        return results;
    }

    private static string NuGetSidecarPath(string zipPath) => Path.ChangeExtension(zipPath, ".nuget.json");

    private static string ResolveZipFilename(string name) => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? name : name + ".zip";

    // `package`/`request.NewName` ultimately come from a Management-API caller and aren't validated
    // for path-traversal characters upstream - a rooted value (e.g. "C:/Windows/..." or "/etc/...")
    // would otherwise make Path.Combine ignore PackagesRootDirectory entirely and resolve outside it.
    // Same containment pattern as OrbitMesh.Edge's PackageInstance.ResolveContained.
    private bool TryResolveZipPath(string package, out string path)
    {
        var root = Path.GetFullPath(options.CurrentValue.PackagesRootDirectory);
        var candidate = Path.GetFullPath(Path.Combine(root, ResolveZipFilename(package)));
        if (!IsWithin(root, candidate))
        {
            path = string.Empty;
            return false;
        }
        path = candidate;
        return true;
    }

    private static bool IsWithin(string root, string candidate)
    {
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return candidate == root || candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal);
    }

    // Blocks server-side request forgery via DownloadPackage's caller-supplied URL: resolves the host
    // and rejects it if any resolved address is loopback/link-local/private - link-local specifically
    // covers cloud instance-metadata endpoints (169.254.169.254) that would otherwise hand out
    // credentials to whatever asks. DNS resolution (not just a literal-IP check) matters because a
    // hostname can resolve to an internal address just as easily as a literal one can be typed.
    private static async Task<bool> IsPubliclyRoutableUrlAsync(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(uri.Host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(uri.Host);
        }
        catch
        {
            return false;
        }
        return addresses.Length > 0 && Array.TrueForAll(addresses, IsPublicAddress);
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
        {
            return false;
        }
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            // IsIPv6SiteLocal only covers the deprecated fec0::/10 range - fc00::/7 (Unique Local
            // Addresses, the IPv6 equivalent of RFC1918 private IPv4) has no equivalent IPAddress
            // property and needs its own check, or an internal IPv6-only network is reachable here.
            return address.AddressFamily != AddressFamily.InterNetworkV6 || (address.GetAddressBytes()[0] & 0xfe) != 0xfc;
        }
        var b = address.GetAddressBytes();
        return b[0] switch
        {
            0 => false,       // 0.0.0.0/8
            10 => false,      // 10.0.0.0/8
            127 => false,     // 127.0.0.0/8 (loopback, redundant with IsLoopback above)
            169 => b[1] != 254, // 169.254.0.0/16 (link-local, incl. cloud instance metadata)
            172 => b[1] is < 16 or > 31, // 172.16.0.0/12
            192 => b[1] != 168, // 192.168.0.0/16
            _ => true
        };
    }

    private static object BuildPackageRepositoryEntry(string filePath)
    {
        var manifest = PackageZipHelper.GetManifest(filePath);
        var info = new FileInfo(filePath);
        return new
        {
            Filename = info.Name,
            LastUpdate = info.LastWriteTime,
            Size = info.Length,
            PackageInfo = manifest == null ? null : new
            {
                manifest.Name,
                manifest.Version,
                manifest.Author,
                manifest.Description,
                manifest.Icon,
                manifest.URL,
                manifest.PackageUrl,
                OrbitMeshVersion = manifest.Compatibility.OrbitMeshVersion,
                DotNetTargetPlatform = manifest.Compatibility.DotNetTargetPlatform,
                Runtime = manifest.Runtime.ToString(),
                // The console's "Edit Settings" modal reads Name/Type/IsRequired/Description/DefaultValue
                // off each entry (PackageSettings.html) to render type-aware inputs (checkbox, timepicker,
                // CodeMirror for JSON/XML, ...) - omitting this left it falling back to plain text boxes.
                Settings = manifest.Settings.Select(s => new
                {
                    s.Name,
                    Type = s.Type.ToString(),
                    s.IsRequired,
                    Description = s.Description ?? string.Empty,
                    DefaultValue = s.DefaultSettingValue
                })
            }
        };
    }

    [HttpPost("packages/{package}")]
    [RequiresScope(OrbitMeshScope.PackagesDeploy)]
    public IActionResult RenamePackageFile(string package, [FromBody] RenamePackageRequest request)
    {
        if (!TryResolveZipPath(package, out var fromPath) || !System.IO.File.Exists(fromPath))
        {
            return NotFound($"Package '{package}' not found");
        }
        if (!TryResolveZipPath(request.NewName, out var toPath))
        {
            return BadRequest($"'{request.NewName}' is not a valid package name.");
        }
        System.IO.File.Move(fromPath, toPath);
        return Ok();
    }

    public sealed class RenamePackageRequest
    {
        public required string NewName { get; set; }
    }

    [HttpDelete("packages/{package}")]
    [RequiresScope(OrbitMeshScope.PackagesDeploy)]
    public IActionResult RemovePackageFile(string package)
    {
        if (!TryResolveZipPath(package, out var path) || !System.IO.File.Exists(path))
        {
            return NotFound();
        }
        System.IO.File.Delete(path);
        return Ok();
    }

    [HttpGet("packages/{package}/icon")]
    [RequiresScope(OrbitMeshScope.PackagesRead, OrbitMeshScope.PackagesDeploy)]
    public IActionResult GetPackageIcon(string package)
    {
        if (!TryResolveZipPath(package, out var path))
        {
            return NotFound();
        }
        var manifest = System.IO.File.Exists(path) ? PackageZipHelper.GetManifest(path) : null;
        var iconBytes = !string.IsNullOrEmpty(manifest?.Icon) ? PackageZipHelper.GetEntryContent(path, manifest.Icon) : null;
        return iconBytes != null ? File(iconBytes, "image/png") : NotFound();
    }

    [HttpGet("packages/{package}/settings/{settingName}/xsd")]
    [RequiresScope(OrbitMeshScope.PackagesRead)]
    public IActionResult GetSettingXsdSchema(string package, string settingName)
    {
        if (!TryResolveZipPath(package, out var path))
        {
            return NotFound();
        }
        var manifest = System.IO.File.Exists(path) ? PackageZipHelper.GetManifest(path) : null;
        var setting = manifest?.Settings.FirstOrDefault(s => s.Name.Equals(settingName, StringComparison.OrdinalIgnoreCase));
        var content = !string.IsNullOrEmpty(setting?.SchemaXSD) ? PackageZipHelper.GetEntryContent(path, setting.SchemaXSD) : null;
        return content != null ? File(content, "text/xml") : NotFound();
    }

    [HttpPost("download-package")]
    [RequiresScope(OrbitMeshScope.PackagesDeploy)]
    public async Task<IActionResult> DownloadPackage([FromServices] IHttpClientFactory httpClientFactory, [FromBody] DownloadPackageRequest request)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || !await IsPubliclyRoutableUrlAsync(uri))
        {
            return BadRequest("The URL must be a public http/https address (not loopback, link-local, or a private network range).");
        }

        var tempFile = Path.GetTempFileName();
        try
        {
            // AllowAutoRedirect disabled deliberately: a server that passes the check above could still
            // 30x to an internal/loopback address, and following that would defeat it (classic SSRF
            // redirect bypass) - a redirect is just treated as a failed download instead of chased.
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler);
            using (var response = await client.GetAsync(uri))
            {
                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode((int)response.StatusCode, $"Failed to download the package ({(int)response.StatusCode}).");
                }
                await using var stream = await response.Content.ReadAsStreamAsync();
                await using var file = System.IO.File.Create(tempFile);
                await stream.CopyToAsync(file);
            }
            var manifest = PackageZipHelper.GetManifest(tempFile);
            if (manifest == null)
            {
                System.IO.File.Delete(tempFile);
                return UnprocessableEntity("No PackageInfo.xml manifest found in this package.");
            }
            if (!TryResolveZipPath(manifest.Name, out var destination))
            {
                System.IO.File.Delete(tempFile);
                return BadRequest($"'{manifest.Name}' is not a valid package name.");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (System.IO.File.Exists(destination))
            {
                System.IO.File.Delete(destination);
            }
            System.IO.File.Move(tempFile, destination);
            return Ok();
        }
        catch (Exception ex)
        {
            if (System.IO.File.Exists(tempFile))
            {
                System.IO.File.Delete(tempFile);
            }
            return StatusCode(500, ex.Message);
        }
    }

    public sealed class DownloadPackageRequest
    {
        public required string Url { get; set; }
    }

    [HttpPost("upload-package")]
    [RequestSizeLimit(200_000_000)]
    [RequiresScope(OrbitMeshScope.PackagesDeploy)]
    public async Task<IActionResult> UploadPackage(IFormFile file)
    {
        if (file.Length == 0)
        {
            return BadRequest("Empty file");
        }
        var dir = Path.GetFullPath(options.CurrentValue.PackagesRootDirectory);
        Directory.CreateDirectory(dir);
        var tempPath = Path.GetTempFileName();
        await using (var stream = System.IO.File.Create(tempPath))
        {
            await file.CopyToAsync(stream);
        }
        var manifest = PackageZipHelper.GetManifest(tempPath);
        if (manifest == null)
        {
            System.IO.File.Delete(tempPath);
            return UnprocessableEntity("No PackageInfo.xml manifest found in this package.");
        }
        var destination = Path.Combine(dir, $"{manifest.Name}.zip");
        if (System.IO.File.Exists(destination))
        {
            System.IO.File.Delete(destination);
        }
        System.IO.File.Move(tempPath, destination);
        return Ok();
    }

    // No license system in this fork (see project notes) - the console only reads .Type for display,
    // so a permissive stub keeps the UI happy without reintroducing the original phone-home licensing.
    [HttpGet("license")]
    public ActionResult<object> GetLicense() => new { Type = 3, Name = "Personal (unrestricted fork)", ExpirationDate = (DateTime?)null };

    [HttpPost("license")]
    public IActionResult SetLicense() => Ok();
}
