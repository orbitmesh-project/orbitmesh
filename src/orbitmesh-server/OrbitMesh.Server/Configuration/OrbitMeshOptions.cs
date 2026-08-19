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

    public List<string> ListenUrls { get; set; } = new() { "http://+:8088" };

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

    public List<CredentialOptions> Credentials { get; set; } = new();

    public List<EdgeOptions> Edges { get; set; } = new();

    public List<VariableOptions> Variables { get; set; } = new();

    public UpdateOptions UpdateOptions { get; set; } = new();

    // Empty by default - there's no fixed, stable public feed URL to ship as a baked-in default yet.
    // Ordered: when the same package id/version exists on more than one feed, the first one listed wins.
    public List<NuGetFeedOptions> NuGetFeeds { get; set; } = new();
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
// Human: a Console login. Stored as a salted PBKDF2 hash (see AccessKeyHasher) - nothing reads a
// Human credential's key back out, so there's no reason to keep it recoverable, and a human-chosen
// password benefits far more from a deliberately slow hash than a high-entropy random machine token does.
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
    public DateTime? LastUsedUtc { get; set; }
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
