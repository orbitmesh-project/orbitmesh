using System.Text.Json.Serialization;
using OrbitMesh;
using OrbitMesh.Updating;

namespace OrbitMesh.Server.Configuration;

/// <summary>Bound from (and, via <see cref="Services.IOrbitMeshConfigWriter"/>, written back to)
/// the "OrbitMesh" section of appsettings.json - this is the server's live, mutable configuration.</summary>
public sealed class OrbitMeshOptions
{
    public const string SectionName = "OrbitMesh";

    public string PackagesRootDirectory { get; set; } = "packages";

    // "https://+:8443" works as an entry too, given a certificate in the standard Kestrel section (e.g.
    // Kestrel:Certificates:Default:Path / :Password) - nothing OrbitMesh-specific to configure beyond that.
    public List<string> ListenUrls { get; set; } = new() { "http://+:8088" };

    // IP addresses of reverse proxies in front of this server whose X-Forwarded-For / X-Forwarded-Proto
    // headers are trusted. Empty (the default) trusts none, so RemoteIpAddress is always the direct peer.
    // Behind a proxy that is NOT listed here, every client appears to come from the proxy's address:
    // the failed-login lockout then becomes global (ten bad attempts from anywhere lock everyone out) and
    // a FileServers entry marked LocalhostOnly is exposed to every client of a same-host proxy.
    public List<string> TrustedProxies { get; set; } = new();

    // A list rather than a single entry so more than one static site (e.g. the admin console at
    // /WebConsole and a separate Consumer dashboard at /dashboard) can be served side by side by this
    // same process/port.
    public List<FileServerOptions> FileServers { get; set; } = new();

    // Empty by default: browsers block cross-origin JS requests without CORS headers, which is the safe
    // default here since auth travels via header/querystring (not a cookie) - a leaked AccessKey could
    // otherwise be used from any malicious page in a visitor's browser. Anything served from FileServers
    // is same-origin and never needs an entry here. Only add origins for a genuinely cross-origin caller
    // (e.g. a Consumer app hosted elsewhere, or one of the FileServers entries served from a different
    // port during local development) that you trust with that key.
    public List<string> AllowedOrigins { get; set; } = new();

    public RecoveryOptions RecoveryOptions { get; set; } = new();

    public AuthOptions Auth { get; set; } = new();

    public List<CredentialOptions> Credentials { get; set; } = new();

    public List<EdgeOptions> Edges { get; set; } = new();

    public List<VariableOptions> Variables { get; set; } = new();

    public List<ScheduledTaskOptions> ScheduledTasks { get; set; } = new();

    public UpdateOptions UpdateOptions { get; set; } = new();

    // Empty by default - there's no fixed, stable public feed URL to ship as a baked-in default yet.
    // Ordered: when the same package id/version exists on more than one feed, the first one listed wins.
    public List<NuGetFeedOptions> NuGetFeeds { get; set; } = new();
}

/// <summary>How humans sign in to the Console: a username/password login (Human credentials) and/or
/// single sign-on through an OpenID Connect provider such as Authelia. Machine credentials (Edges,
/// packages, dashboards) are unaffected - they keep presenting their AccessKey.</summary>
public sealed class AuthOptions
{
    /// <summary>False hides the password form and refuses POST rest/auth/login - only sensible once OIDC
    /// works, since it also removes the break-glass account.</summary>
    public bool LocalLoginEnabled { get; set; } = true;

    /// <summary>A session unused for this long expires (any request, or the Console's keep-alive, counts as use).</summary>
    public int SessionIdleMinutes { get; set; } = 30;

    /// <summary>A session never lives longer than this, however active.</summary>
    public int SessionMaxHours { get; set; } = 24;

    public OidcOptions Oidc { get; set; } = new();
}

/// <summary>OpenID Connect single sign-on (authorization-code flow + PKCE, the Server being a confidential
/// client). Changing anything but <see cref="RoleMappings"/> needs a Server restart.</summary>
public sealed class OidcOptions
{
    public bool Enable { get; set; }

    /// <summary>Label of the Console's sign-in button.</summary>
    public string DisplayName { get; set; } = "Single sign-on";

    /// <summary>The provider's issuer URL, e.g. "https://auth.example.com" (its /.well-known/openid-configuration is read).</summary>
    public string Authority { get; set; } = string.Empty;

    public string ClientId { get; set; } = "orbitmesh";

    /// <summary>The client secret as the provider knows it (the plain value, not Authelia's hash). Can also be
    /// supplied through the OrbitMesh__Auth__Oidc__ClientSecret environment variable.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    public bool RequireHttpsMetadata { get; set; } = true;

    public List<string> RequestedScopes { get; set; } = ["openid", "profile", "email", "groups"];

    /// <summary>Claim holding the login shown in the Console and the audit log; the session is named "oidc:&lt;value&gt;".</summary>
    public string UsernameClaim { get; set; } = "preferred_username";

    public string GroupsClaim { get; set; } = "groups";

    /// <summary>Who may sign in, and with which scopes: a user gets the union of the scopes of every mapping
    /// whose Group they belong to, and is refused when none matches. Evaluated live, so editing a mapping
    /// applies to open sessions too.</summary>
    public List<OidcRoleMapping> RoleMappings { get; set; } = new();

    /// <summary>Enabled and complete enough to work. A half-configured provider counts as off, so it can't
    /// close first-run setup (see ManagementController.GetSetupStatus) without offering another way in.</summary>
    [JsonIgnore]
    public bool IsUsable => Enable
        && !string.IsNullOrWhiteSpace(Authority)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrEmpty(ClientSecret);
}

public sealed class OidcRoleMapping
{
    public string Group { get; set; } = string.Empty;

    /// <summary>OrbitMeshScope names; "*" stands for every scope.</summary>
    public List<string> Scopes { get; set; } = [];
}

/// <summary>A NuGet V3 feed OrbitMesh packages can be browsed from and downloaded from - e.g. a
/// self-hosted Pépite instance. Read-only from OrbitMesh's side: publishing is a separate concern
/// handled by the packages' own release tooling, not by the Server.</summary>
public sealed class NuGetFeedOptions
{
    public required string Name { get; set; }

    /// <summary>The feed's service index, e.g. "https://packages.example.com/feeds/orbitmesh/v3/index.json".</summary>
    public required string ServiceIndexUrl { get; set; }

    /// <summary>Required only for a private feed's Basic-auth read access - the API key is the
    /// password, never the account's own password (see the feed server's own auth conventions).</summary>
    public string? Username { get; set; }

    public string? ApiKey { get; set; }

    public bool Enable { get; set; } = true;
}

public sealed class FileServerOptions
{
    public bool Enable { get; set; }

    public bool LocalhostOnly { get; set; }

    public string Path { get; set; } = "/WebConsole";

    public string PhysicalPath { get; set; } = "console";

    // Empty (the default) disables update checking for this site entirely - matches
    // UpdateOptions.ServerUrl's own "empty means opt-out" convention.
    public string? UpdateProjectSlug { get; set; }

    // Filenames (relative to PhysicalPath) an update must carry forward instead of taking
    // whatever the release zip ships in their place - e.g. a device-specific config.json holding
    // that site's own server URL/access key, which no release build could know in advance.
    public List<string> PreserveFiles { get; set; } = new();

    // A client-side-routed single-page app (e.g. a Vue Router app using history-mode URLs like
    // /console-next/packages) needs every sub-path that isn't a real file served back index.html
    // instead of 404ing - the router itself resolves the path once the app loads. False by default
    // since a plain multi-page site (e.g. a static dashboard) never wants this.
    public bool IsSpa { get; set; }
}

// Machine (default): an Edge or package's bearer token. Kept in plaintext at rest - the Server
// hands it back out verbatim (see PackageDescription.AccessKey / PackageInstance's launch args) so a
// package can open its own connection with it, which a one-way hash could never support.
//
// Human: a Console login. AccessKey holds a salted PBKDF2 hash of the password (see PasswordHasher) -
// nothing reads it back out, and a human-chosen password benefits far more from a deliberately slow hash
// than a high-entropy random machine token does. A Human credential is never presented per request: it
// signs in (AuthController) and the session (SessionStore) carries the requests after that. (Hashes made
// by AccessKeyHasher, before sessions existed, are still accepted at sign-in and upgraded there.)
public enum CredentialKind
{
    Machine,
    Human
}

public sealed class CredentialOptions
{
    public required string Name { get; set; }

    public required string AccessKey { get; set; }

    public CredentialKind Kind { get; set; } = CredentialKind.Machine;

    public bool Enable { get; set; } = true;

    // Named permission scopes (see OrbitMeshScope) - PAT-style. AuthorizationOptions (Messages/Groups/
    // TelemetryItems rules) still applies underneath as a finer per-target filter once a scope already
    // allows the action class.
    public List<string> Scopes { get; set; } = [];

    public AuthorizationOptions Authorizations { get; set; } = new();

    // Updated on a delay by CredentialUsageFlushService, not on every request - see there for why.
    //
    // DateTimeOffset, not DateTime: see ScheduledTaskOptions.LastRunUtc's comment - the Configuration
    // binder (IOptionsMonitor's read path) silently corrupts a "...Z" DateTime string's Kind and
    // clock value on every read. DateTimeOffset round-trips through the same binder correctly.
    public DateTimeOffset? LastUsedUtc { get; set; }
}

public sealed class AuthorizationOptions
{
    public AuthorizationRuleSet<MessageAuthorizationRule> Messages { get; set; } = new();

    public AuthorizationRuleSet<GroupAuthorizationRule> Groups { get; set; } = new();

    public AuthorizationRuleSet<TelemetryItemAuthorizationRule> TelemetryItems { get; set; } = new();
}

public enum AuthorizationType
{
    Allow,
    Deny
}

public sealed class AuthorizationRuleSet<TRule>
{
    public AuthorizationType DefaultAuthorization { get; set; } = AuthorizationType.Allow;

    public List<TRule> Rules { get; set; } = new();
}

public sealed class MessageAuthorizationRule
{
    public AuthorizationType Authorization { get; set; }

    public OrbitMesh.MessageScope.ScopeType Scope { get; set; }

    public string? Args { get; set; }

    public string? MessageKey { get; set; }
}

public sealed class GroupAuthorizationRule
{
    public AuthorizationType Authorization { get; set; }

    public required string GroupName { get; set; }
}

public sealed class TelemetryItemAuthorizationRule
{
    public AuthorizationType Authorization { get; set; }

    public string EdgeName { get; set; } = "*";

    public string PackageName { get; set; } = "*";

    public string Name { get; set; } = "*";
}

public sealed class EdgeOptions
{
    public required string Name { get; set; }

    public string? Credential { get; set; }

    // Set when this Edge is approved from the pending list (see IPendingEdgeRegistry) - null for a
    // hand-configured edge. Not enforced as a CheckAccess gate; AccessKey alone still gates access.
    public string? InstanceId { get; set; }

    public List<PackageOptions> Packages { get; set; } = new();
}

public sealed class PackageOptions
{
    public required string Name { get; set; }

    public string? Credential { get; set; }

    public string? PackageFile { get; set; }

    public bool Enable { get; set; } = true;

    public bool AutoStart { get; set; }

    public RecoveryOptions? RecoveryOptions { get; set; }

    public List<string> Groups { get; set; } = new();

    public Dictionary<string, string> Settings { get; set; } = new();
}

// Referenced from a package's Settings via a "{Name}" token - see GetSettings' substituteVariables.
public sealed class VariableOptions
{
    public required string Name { get; set; }

    // Plaintext if !IsSecret; AccessKeyCipher-encrypted (ENC$...) if IsSecret - same cipher/key file as
    // Machine credentials, reused as-is rather than a second encryption scheme.
    public required string Value { get; set; }

    public bool IsSecret { get; set; }
}

// Fires a single message (see ScheduledTaskRunner) on a cron schedule - the automation equivalent of
// a human opening the Console's Messages page and clicking Invoke. Runs as Credential, so it's bound
// by that credential's own Authorizations/Scopes (messages:execute) exactly like any other sender -
// no bypass of the permission model just because the trigger is a timer instead of a person.
public sealed class ScheduledTaskOptions
{
    public required string Name { get; set; }

    public bool Enable { get; set; } = true;

    // Standard 5-field cron syntax (minute hour day month day-of-week), parsed by Cronos.
    public required string CronExpression { get; set; }

    public required string Credential { get; set; }

    // If false (the default), an occurrence missed while the Server was down/updating is simply
    // skipped - the next normal occurrence still fires on schedule. If true, one catch-up send fires
    // on the next tick after downtime (once, regardless of how many occurrences were actually missed -
    // this is meant to bring state back in line, not replay every missed day's action).
    public bool CatchUpIfMissed { get; set; }

    public required string EdgeName { get; set; }

    public required string PackageName { get; set; }

    // The handler's fully-qualified key (e.g. "TPLinkSmartHome/SetPower") - see MessageHandlerAttribute.
    public required string MessageKey { get; set; }

    // Raw JSON text (same convention as PackageOptions.Settings), e.g. "[\"deviceId123\", false]" -
    // null for a handler that takes no parameters.
    public string? Data { get; set; }

    // Updated by ScheduledTaskRunner after every fire (or skipped-but-acknowledged occurrence) - not
    // meant to be hand-edited, but visible so an admin can see when a task last actually ran.
    public DateTimeOffset? LastRunUtc { get; set; }
}
