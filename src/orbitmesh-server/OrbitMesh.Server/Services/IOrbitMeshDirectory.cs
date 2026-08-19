using OrbitMesh.Server.Configuration;
using Microsoft.Extensions.Options;

namespace OrbitMesh.Server.Services;

/// <summary>
/// Config-derived queries and access-control checks. Replaces the static <c>OrbitMeshHelper</c>
/// (which read a custom <c>System.Configuration</c> XML section) with a service backed by
/// <see cref="IOptionsMonitor{TOptions}"/> so configuration changes hot-reload without a manual
/// <c>FileSystemWatcher</c>/<c>ConfigurationManager.RefreshSection</c> dance.
/// </summary>
public interface IOrbitMeshDirectory
{
    OrbitMeshOptions Current { get; }

    string GetPackageInstanceId(string edgeName, string packageName);

    bool CheckAccess(string? edgeName, string? packageName, string? accessKey, OrbitMeshAccessType accessType);

    bool CheckMessageGroupAuthorization(string? accessKey, string group);

    bool CheckMessageAuthorization(string? accessKey, MessageScope? scope, string messageKey);

    bool CheckTelemetryItemAuthorization(string? accessKey, string edgeName = "*", string packageName = "*", string name = "*");

    List<string> GetGroups(string edgeName, string packageName);

    /// <summary>substituteVariables replaces "{Name}" tokens with the matching Variable's value -
    /// only for delivery to a connected package, never for the Console's own read/write.</summary>
    Dictionary<string, string> GetSettings(string edgeName, string packageName, bool substituteVariables = false);

    bool EdgeExists(string edgeName);

    /// <summary>Resolves an AccessKey back to its credential name, for logging/audit purposes only.</summary>
    string? GetCredentialName(string? accessKey);

    /// <summary>Resolves an AccessKey back to its credential, for scope checks (see
    /// Services.RequiresScopeAttribute and Hubs.ControlHub).</summary>
    CredentialOptions? FindCredential(string? accessKey);

    List<string> GetPackageNames();

    List<PackageDescription> GetPackagesList(string edgeName, bool includeAccessKey = false);
}

public sealed class OrbitMeshDirectory(IOptionsMonitor<OrbitMeshOptions> options, AccessKeyCipher cipher) : IOrbitMeshDirectory
{
    public OrbitMeshOptions Current => options.CurrentValue;

    public string GetPackageInstanceId(string edgeName, string packageName) => $"{edgeName}/{packageName}";

    public bool CheckAccess(string? edgeName, string? packageName, string? accessKey, OrbitMeshAccessType accessType)
    {
        if (string.IsNullOrEmpty(edgeName) || string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(accessKey))
        {
            return false;
        }

        var config = Current;

        if (edgeName.Equals(OrbitMeshDefaultNames.ConsumerEdgeName, StringComparison.OrdinalIgnoreCase)
            || (edgeName.Equals(OrbitMeshDefaultNames.ControllerEdgeName, StringComparison.OrdinalIgnoreCase) && accessType == OrbitMeshAccessType.Controller)
            || (edgeName.Equals(OrbitMeshDefaultNames.ManagementEdgeName, StringComparison.OrdinalIgnoreCase) && accessType == OrbitMeshAccessType.Management))
        {
            return config.Credentials.Any(c => CanAccess(c, accessType) && MatchesAccessKey(c, accessKey));
        }

        if (edgeName.Equals(OrbitMeshDefaultNames.DeveloperEdgeName, StringComparison.OrdinalIgnoreCase))
        {
            return config.Credentials.Any(c => CanAccess(c, accessType) && c.Scopes.Contains(OrbitMeshScope.Developer) && MatchesAccessKey(c, accessKey));
        }

        var edge = config.Edges.FirstOrDefault(s => s.Name.Equals(edgeName, StringComparison.OrdinalIgnoreCase));
        if (edge == null || string.IsNullOrEmpty(edge.Credential))
        {
            return false;
        }

        if (!packageName.Equals(OrbitMeshDefaultNames.EdgePackageName, StringComparison.OrdinalIgnoreCase))
        {
            var package = edge.Packages.FirstOrDefault(p => p.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase));
            if (package == null)
            {
                return false;
            }
            if (!string.IsNullOrEmpty(package.Credential))
            {
                return config.Credentials.Any(c => c.Name.Equals(package.Credential, StringComparison.OrdinalIgnoreCase) && CanAccess(c, accessType) && MatchesAccessKey(c, accessKey));
            }
        }

        return config.Credentials.Any(c => c.Name.Equals(edge.Credential, StringComparison.OrdinalIgnoreCase) && CanAccess(c, accessType) && MatchesAccessKey(c, accessKey));
    }

    // Human credentials verify against a PBKDF2 hash (AccessKeyHasher, one-way - nothing ever reads a
    // Human credential's key back out). Machine credentials are AES-encrypted at rest (AccessKeyCipher,
    // reversible - the Server hands the key back out verbatim to Edges/packages, see
    // CredentialOptions.Kind), decrypted here for comparison; a legacy plaintext value (predating this
    // encryption, or hand-edited into appsettings.json) still compares directly so existing
    // credentials keep working until OrbitMeshConfigWriter's startup migration re-encrypts them.
    private bool MatchesAccessKey(CredentialOptions credential, string presentedAccessKey)
    {
        if (credential.Kind == CredentialKind.Human)
        {
            return AccessKeyHasher.IsHashed(credential.AccessKey) && AccessKeyHasher.Verify(credential.AccessKey, presentedAccessKey);
        }
        if (AccessKeyCipher.IsEncrypted(credential.AccessKey))
        {
            try
            {
                return cipher.Decrypt(credential.AccessKey) == presentedAccessKey;
            }
            // A decrypt failure (malformed or wrong key) just means "not a match", not a crash.
            catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException)
            {
                return false;
            }
        }
        return credential.AccessKey == presentedAccessKey;
    }

    private static bool CanAccess(CredentialOptions credential, OrbitMeshAccessType accessType)
    {
        if (!credential.Enable)
        {
            return false;
        }
        // Coarse gate: does this credential hold ANY scope at all? Which specific scope a given
        // endpoint/hub method actually needs is checked separately (see RequiresScopeAttribute and
        // ControlHub's per-method checks) - this only rules out a credential with no business connecting
        // to this hub/API family in the first place (e.g. Enable=true but zero Scopes assigned).
        return accessType switch
        {
            OrbitMeshAccessType.Controller or OrbitMeshAccessType.Management => credential.Scopes.Count > 0,
            _ => true
        };
    }

    public bool CheckMessageGroupAuthorization(string? accessKey, string group)
    {
        if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(group))
        {
            return false;
        }
        return CheckAuthorization(accessKey, credential =>
        {
            var ruleSet = credential.Authorizations.Groups;
            var rule = ruleSet.Rules.FirstOrDefault(r => r.GroupName.Equals(group, StringComparison.Ordinal));
            return rule != null ? rule.Authorization == AuthorizationType.Allow : ruleSet.DefaultAuthorization == AuthorizationType.Allow;
        });
    }

    public bool CheckMessageAuthorization(string? accessKey, MessageScope? scope, string messageKey)
    {
        if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(messageKey) || scope == null)
        {
            return false;
        }
        if (scope.IsSaga && messageKey == OrbitMeshDefaultNames.SagaResponseMessageKey)
        {
            return true;
        }
        return CheckAuthorization(accessKey, credential =>
        {
            var ruleSet = credential.Authorizations.Messages;
            var defaultAuthorization = ruleSet.DefaultAuthorization;
            foreach (var rule in ruleSet.Rules)
            {
                if (rule.Scope != scope.Scope) continue;
                if (!string.IsNullOrEmpty(rule.Args) && !scope.Args.Any(a => a.Equals(rule.Args, StringComparison.OrdinalIgnoreCase))) continue;
                if (!string.IsNullOrEmpty(rule.MessageKey) && !rule.MessageKey.Equals(messageKey, StringComparison.OrdinalIgnoreCase)) continue;

                if (defaultAuthorization == AuthorizationType.Allow && rule.Authorization == AuthorizationType.Deny) return false;
                if (defaultAuthorization == AuthorizationType.Deny && rule.Authorization == AuthorizationType.Allow) return true;
            }
            return defaultAuthorization == AuthorizationType.Allow;
        });
    }

    public bool CheckTelemetryItemAuthorization(string? accessKey, string edgeName = "*", string packageName = "*", string name = "*")
    {
        if (string.IsNullOrEmpty(accessKey))
        {
            return false;
        }
        return CheckAuthorization(accessKey, credential =>
        {
            var ruleSet = credential.Authorizations.TelemetryItems;
            var defaultAuthorization = ruleSet.DefaultAuthorization;
            foreach (var rule in ruleSet.Rules)
            {
                if (rule.EdgeName != "*" && !edgeName.Equals(rule.EdgeName, StringComparison.OrdinalIgnoreCase)) continue;
                if (rule.PackageName != "*" && !packageName.Equals(rule.PackageName, StringComparison.OrdinalIgnoreCase)) continue;
                if (rule.Name != "*" && !name.Equals(rule.Name, StringComparison.OrdinalIgnoreCase)) continue;

                if (defaultAuthorization == AuthorizationType.Allow && rule.Authorization == AuthorizationType.Deny) return false;
                if (defaultAuthorization == AuthorizationType.Deny && rule.Authorization == AuthorizationType.Allow) return true;
            }
            return defaultAuthorization == AuthorizationType.Allow;
        });
    }

    private bool CheckAuthorization(string accessKey, Func<CredentialOptions, bool> predicate) =>
        Current.Credentials.Any(c => MatchesAccessKey(c, accessKey) && c.Enable && predicate(c));

    public List<string> GetGroups(string edgeName, string packageName)
    {
        var edge = Current.Edges.FirstOrDefault(s => s.Name.Equals(edgeName, StringComparison.OrdinalIgnoreCase));
        var package = edge?.Packages.FirstOrDefault(p => p.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase));
        return package?.Groups.ToList() ?? new List<string>();
    }

    public Dictionary<string, string> GetSettings(string edgeName, string packageName, bool substituteVariables = false)
    {
        var edge = Current.Edges.FirstOrDefault(s => s.Name.Equals(edgeName, StringComparison.OrdinalIgnoreCase));
        var package = edge?.Packages.FirstOrDefault(p => p.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase));
        var result = package == null ? new Dictionary<string, string>() : new Dictionary<string, string>(package.Settings);
        if (substituteVariables)
        {
            SubstituteVariableTokens(result);
        }
        return result;
    }

    private static readonly System.Text.RegularExpressions.Regex VariableTokenPattern = new(@"\{(\w+)\}", System.Text.RegularExpressions.RegexOptions.Compiled);

    // A key not found in the Variables list is left as literal "{Name}" text - easier to notice and
    // fix (a typo, or a Variable that got renamed/removed) than a silently blanked-out value.
    private void SubstituteVariableTokens(Dictionary<string, string> settings)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in Current.Variables)
        {
            if (!variable.IsSecret)
            {
                values[variable.Name] = variable.Value;
                continue;
            }
            try
            {
                values[variable.Name] = cipher.Decrypt(variable.Value);
            }
            catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException)
            {
                // Same stance as MatchesAccessKey: can't decrypt, so this token just stays unresolved.
            }
        }
        foreach (var key in settings.Keys.ToList())
        {
            settings[key] = VariableTokenPattern.Replace(settings[key], m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
        }
    }

    public bool EdgeExists(string edgeName) =>
        Current.Edges.Any(s => s.Name.Equals(edgeName, StringComparison.OrdinalIgnoreCase));

    public string? GetCredentialName(string? accessKey) => FindCredential(accessKey)?.Name;

    public CredentialOptions? FindCredential(string? accessKey) =>
        string.IsNullOrEmpty(accessKey) ? null : Current.Credentials.FirstOrDefault(c => MatchesAccessKey(c, accessKey));

    public List<string> GetPackageNames() =>
        Current.Edges.SelectMany(s => s.Packages.Select(p => p.Name)).Distinct().ToList();

    public List<PackageDescription> GetPackagesList(string edgeName, bool includeAccessKey = false)
    {
        var config = Current;
        var edge = config.Edges.FirstOrDefault(s => s.Name.Equals(edgeName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Unknown edge name", nameof(edgeName));

        return edge.Packages.Where(p => p.Enable).Select(p => new PackageDescription
        {
            Name = p.Name,
            PackageFile = p.PackageFile ?? $"{p.Name}.zip",
            AutoStart = p.AutoStart,
            RecoveryOptions = p.RecoveryOptions ?? config.RecoveryOptions,
            AccessKey = includeAccessKey
                ? DecryptForHandoff(config.Credentials.FirstOrDefault(c => c.Name.Equals(p.Credential, StringComparison.OrdinalIgnoreCase)))
                : null
        }).ToList();
    }

    // The one legitimate "read a Machine key back out" path (see AccessKeyCipher's remarks) - decrypts
    // for the Edge to hand to a package instance's own connection, same legacy-plaintext fallback as
    // MatchesAccessKey for credentials that haven't been migrated yet.
    private string? DecryptForHandoff(CredentialOptions? credential)
    {
        if (credential == null)
        {
            return null;
        }
        if (!AccessKeyCipher.IsEncrypted(credential.AccessKey))
        {
            return credential.AccessKey;
        }
        try
        {
            return cipher.Decrypt(credential.AccessKey);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
