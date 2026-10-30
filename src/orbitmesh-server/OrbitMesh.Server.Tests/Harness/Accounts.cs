using System.Text.Json.Nodes;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using OrbitMesh.Server;
using OrbitMesh.Server.Services;

namespace OrbitMesh.Server.Tests.Harness;

public sealed record MachineAccount(string Name, string Key, string[] Scopes, bool Enable = true, JsonObject? Authorizations = null);

public sealed record HumanAccount(string Name, string Password, string[] Scopes, bool Enable = true, bool LegacyHash = false);

/// <summary>The identities every test server starts with, and the configuration file that declares them. The
/// names say what each one is for; the secrets are test values that exist nowhere else.</summary>
public static class Accounts
{
    public static readonly string[] ReadScopes = OrbitMeshScope.ForPeople.Where(s => s.EndsWith(":read", StringComparison.Ordinal)).ToArray();

    // Machine credentials (an Edge, a package, a dashboard, a script).
    public static readonly MachineAccount MachineAll = new("m-all", "key-m-all-3c1a7b9f", OrbitMeshScope.ForPeople.ToArray());
    public static readonly MachineAccount MachineRead = new("m-read", "key-m-read-8d2e4f10", [OrbitMeshScope.TelemetryRead, OrbitMeshScope.EdgesRead]);
    public static readonly MachineAccount MachineNoScope = new("m-noscope", "key-m-noscope-51b7c2aa", []);
    public static readonly MachineAccount MachineDisabled = new("m-off", "key-m-off-77e0d9c3", [OrbitMeshScope.EdgesRead], Enable: false);
    public static readonly MachineAccount OnlyConfigRead = new("m-config-read", "key-m-cfg-1f6a0b52", [OrbitMeshScope.ConfigurationRead]);
    public static readonly MachineAccount OnlyCredentialsRead = new("m-cred-read", "key-m-cred-92c4e7d8", [OrbitMeshScope.CredentialsRead]);
    public static readonly MachineAccount OnlyEdgesRead = new("m-edges-read", "key-m-edges-6b3d85e1", [OrbitMeshScope.EdgesRead]);
    public static readonly MachineAccount EdgeA = new("edge-a-cred", "key-edge-a-0a9c3f77", [OrbitMeshScope.EdgeConnect]);
    public static readonly MachineAccount EdgeB = new("edge-b-cred", "key-edge-b-e41d6a09", [OrbitMeshScope.EdgeConnect]);

    // A dashboard that may only reach the "lights" package and the "living-room" group, and see everything
    // but the "secret-edge" edge and the telemetry items called "Password".
    public static readonly MachineAccount RulesClosed = new("m-rules-closed", "key-m-rules-closed-5e8a1d03", [OrbitMeshScope.MessagesExecute],
        Authorizations: new JsonObject
        {
            ["messages"] = new JsonObject
            {
                ["defaultAuthorization"] = "Deny",
                ["rules"] = new JsonArray(
                    new JsonObject { ["authorization"] = "Allow", ["scope"] = "Package", ["args"] = "lights" },
                    new JsonObject { ["authorization"] = "Allow", ["scope"] = "Group", ["args"] = "living-room" })
            },
            ["groups"] = new JsonObject
            {
                ["defaultAuthorization"] = "Deny",
                ["rules"] = new JsonArray(new JsonObject { ["authorization"] = "Allow", ["groupName"] = "living-room" })
            },
            ["telemetryItems"] = new JsonObject
            {
                ["defaultAuthorization"] = "Allow",
                ["rules"] = new JsonArray(
                    new JsonObject { ["authorization"] = "Deny", ["edgeName"] = "secret-edge", ["packageName"] = "*", ["name"] = "*" },
                    new JsonObject { ["authorization"] = "Deny", ["edgeName"] = "*", ["packageName"] = "*", ["name"] = "Password" })
            }
        });

    // Everything is allowed except one message key.
    public static readonly MachineAccount RulesOpen = new("m-rules-open", "key-m-rules-open-b72c9f64", [OrbitMeshScope.MessagesExecute],
        Authorizations: new JsonObject
        {
            ["messages"] = new JsonObject
            {
                ["defaultAuthorization"] = "Allow",
                ["rules"] = new JsonArray(new JsonObject { ["authorization"] = "Deny", ["scope"] = "Package", ["messageKey"] = "Reboot" })
            }
        });

    // People with a local account.
    public static readonly HumanAccount Admin = new("admin", "Admin-Passw0rd-1", OrbitMeshScope.ForPeople.ToArray());
    public static readonly HumanAccount Viewer = new("viewer", "Viewer-Passw0rd-1", ReadScopes);
    public static readonly HumanAccount NoScopes = new("empty", "Empty-Passw0rd-1", []);
    public static readonly HumanAccount Disabled = new("gone", "Gone-Passw0rd-1", [OrbitMeshScope.EdgesRead], Enable: false);
    public static readonly HumanAccount Legacy = new("legacy", "Legacy-Passw0rd-1", ReadScopes, LegacyHash: true);
    public static readonly HumanAccount ConfigWriter = new("cfgwriter", "Cfg-Passw0rd-1", [OrbitMeshScope.ConfigurationRead, OrbitMeshScope.ConfigurationWrite]);

    // Accounts that a test is free to disable, reset or sign out: nobody else uses them.
    public static readonly HumanAccount DisableMe = new("victim-disable", "Victim-Passw0rd-1", [OrbitMeshScope.EdgesRead]);
    public static readonly HumanAccount ResetMe = new("victim-reset", "Victim-Passw0rd-2", [OrbitMeshScope.EdgesRead]);
    public static readonly HumanAccount EditMe = new("victim-edit", "Victim-Passw0rd-3", [OrbitMeshScope.EdgesRead]);
    public static readonly HumanAccount Manager = new("manager", "Manager-Passw0rd-1", [OrbitMeshScope.CredentialsRead, OrbitMeshScope.CredentialsManage, OrbitMeshScope.EdgesRead]);

    public static readonly MachineAccount[] AllMachines =
        [MachineAll, MachineRead, MachineNoScope, MachineDisabled, OnlyConfigRead, OnlyCredentialsRead, OnlyEdgesRead, EdgeA, EdgeB, RulesClosed, RulesOpen];

    public static readonly HumanAccount[] AllHumans = [Admin, Viewer, NoScopes, Disabled, Legacy, ConfigWriter, DisableMe, ResetMe, EditMe, Manager];

    // Single sign-on groups mapped in the default configuration.
    public const string AdminGroup = "ops-admins";
    public const string ViewerGroup = "ops-viewers";
    public const string UnmappedGroup = "interns";
    // Mapped to edges:read; a test is free to change this mapping.
    public const string TempGroup = "ops-temp";
    // Mapped to configuration:read + configuration:write only.
    public const string ConfigGroup = "cfg-writers";

    public const string OidcClientSecret = "super-secret-client-secret";

    // PBKDF2 at 600,000 iterations is deliberately slow; hash each test password once per process.
    private static readonly Dictionary<string, Lazy<string>> PasswordHashes = AllHumans
        .ToDictionary(h => h.Name, h => new Lazy<string>(() => PasswordHasher.Hash(h.Password)));

    private static readonly Dictionary<string, Lazy<string>> LegacyHashes = AllHumans
        .ToDictionary(h => h.Name, h => new Lazy<string>(() => AccessKeyHasher.Hash(PasswordHasher.LegacyAccessKey(h.Name, h.Password))));

    /// <summary>The whole appsettings.json for a test server. Machine keys are written already encrypted so
    /// that starting the server has nothing to migrate (and so writes nothing).</summary>
    public static JsonObject BuildConfiguration(string contentRoot, Action<JsonObject>? customize = null)
    {
        var cipher = new AccessKeyCipher(new TestHostEnvironment(contentRoot));

        var credentials = new JsonArray();
        foreach (var m in AllMachines)
        {
            var machine = Credential(m.Name, cipher.Encrypt(m.Key), "Machine", m.Enable, m.Scopes);
            if (m.Authorizations != null)
            {
                machine["authorizations"] = m.Authorizations.DeepClone();
            }
            credentials.Add(machine);
        }
        foreach (var h in AllHumans)
        {
            var stored = h.LegacyHash ? LegacyHashes[h.Name].Value : PasswordHashes[h.Name].Value;
            credentials.Add(Credential(h.Name, stored, "Human", h.Enable, h.Scopes));
        }

        var orbitmesh = new JsonObject
        {
            ["packagesRootDirectory"] = Path.Combine(contentRoot, "packages"),
            ["trustedProxies"] = new JsonArray(),
            ["fileServers"] = new JsonArray(),
            ["allowedOrigins"] = new JsonArray(),
            ["credentials"] = credentials,
            ["edges"] = new JsonArray
            {
                new JsonObject { ["name"] = "edge-a", ["credential"] = EdgeA.Name, ["packages"] = new JsonArray() },
                new JsonObject { ["name"] = "edge-b", ["credential"] = EdgeB.Name, ["packages"] = new JsonArray() }
            },
            ["auth"] = new JsonObject
            {
                ["localLoginEnabled"] = true,
                ["sessionIdleMinutes"] = 30,
                ["sessionMaxHours"] = 24,
                ["oidc"] = new JsonObject
                {
                    ["enable"] = true,
                    ["displayName"] = "Authelia",
                    // Never contacted: the tests create federated sessions directly, the redirect dance is not exercised.
                    ["authority"] = "https://idp.test.invalid",
                    ["clientId"] = "orbitmesh",
                    ["clientSecret"] = OidcClientSecret,
                    ["requireHttpsMetadata"] = true,
                    ["usernameClaim"] = "preferred_username",
                    ["groupsClaim"] = "groups",
                    ["roleMappings"] = new JsonArray
                    {
                        new JsonObject { ["group"] = AdminGroup, ["scopes"] = new JsonArray("*") },
                        new JsonObject { ["group"] = ViewerGroup, ["scopes"] = new JsonArray(ReadScopes.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray()) },
                        new JsonObject { ["group"] = TempGroup, ["scopes"] = new JsonArray(OrbitMeshScope.EdgesRead) },
                        new JsonObject { ["group"] = ConfigGroup, ["scopes"] = new JsonArray(OrbitMeshScope.ConfigurationRead, OrbitMeshScope.ConfigurationWrite) }
                    }
                }
            }
        };
        customize?.Invoke(orbitmesh);

        return new JsonObject
        {
            ["AllowedHosts"] = "*",
            ["OrbitMesh"] = orbitmesh
        };
    }

    private static JsonObject Credential(string name, string accessKey, string kind, bool enable, string[] scopes) => new()
    {
        ["name"] = name,
        ["accessKey"] = accessKey,
        ["kind"] = kind,
        ["enable"] = enable,
        ["scopes"] = new JsonArray(scopes.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray())
    };

    private sealed class TestHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "OrbitMesh.Server.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
