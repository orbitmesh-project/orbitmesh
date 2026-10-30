using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
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

    /// <summary>Resolves an AccessKey (or a session's pseudo key, see CallerKeys) back to its enabled
    /// credential, for scope checks (see Services.RequiresScopeAttribute and Hubs.ControlHub). A single-sign-on
    /// session resolves to a synthetic credential carrying the scopes its groups map to.</summary>
    CredentialOptions? FindCredential(string? accessKey);

    /// <summary>The identity provider's groups for a single-sign-on session, null for any other caller.</summary>
    IReadOnlyList<string>? GetFederatedGroups(string? accessKey);

    List<string> GetPackageNames();

    List<PackageDescription> GetPackagesList(string edgeName, bool includeAccessKey = false);
}

public sealed class OrbitMeshDirectory : IOrbitMeshDirectory
{
    private readonly IOptionsMonitor<OrbitMeshOptions> options;
    private readonly AccessKeyCipher cipher;
    private readonly SagaRegistry sagaRegistry;
    private readonly SessionStore sessions;

    // SHA-256 of a presented AccessKey -> name of the credential it verified against. Checking a key means
    // an AES decrypt per Machine credential and a PBKDF2 per legacy Human one, and the same key is checked
    // many times a second (every REST call, every hub method); remembering the outcome makes that one
    // dictionary lookup. Only successes are kept (so an attacker can't grow it), the credential is then
    // re-read from the live config on every use (so disabling it, or changing its scopes, applies at once),
    // and the whole thing is dropped whenever the configuration reloads (so a changed or removed key stops
    // working at once too).
    private readonly ConcurrentDictionary<string, string> verifiedKeys = new();
    private const int MaxVerifiedKeys = 1024;

    public OrbitMeshDirectory(IOptionsMonitor<OrbitMeshOptions> options, AccessKeyCipher cipher, SagaRegistry sagaRegistry, SessionStore sessions)
    {
        this.options = options;
        this.cipher = cipher;
        this.sagaRegistry = sagaRegistry;
        this.sessions = sessions;
        options.OnChange(_ => verifiedKeys.Clear());
    }

    public OrbitMeshOptions Current => options.CurrentValue;

    public string GetPackageInstanceId(string edgeName, string packageName) => $"{edgeName}/{packageName}";

    public bool CheckAccess(string? edgeName, string? packageName, string? accessKey, OrbitMeshAccessType accessType)
    {
        if (string.IsNullOrEmpty(edgeName) || string.IsNullOrEmpty(packageName) || string.IsNullOrEmpty(accessKey))
        {
            return false;
        }

        var config = Current;
        var caller = ResolveEnabled(accessKey);
        if (caller == null)
        {
            return false;
        }

        if (edgeName.Equals(OrbitMeshDefaultNames.ConsumerEdgeName, StringComparison.OrdinalIgnoreCase)
            || (edgeName.Equals(OrbitMeshDefaultNames.ControllerEdgeName, StringComparison.OrdinalIgnoreCase) && accessType == OrbitMeshAccessType.Controller)
            || (edgeName.Equals(OrbitMeshDefaultNames.ManagementEdgeName, StringComparison.OrdinalIgnoreCase) && accessType == OrbitMeshAccessType.Management))
        {
            return CanAccess(caller, accessType);
        }

        // A signed-in person's session is only good for the Console's own connections above - never for
        // posing as an Edge, a package or the developer edge.
        if (CallerKeys.IsSessionKey(accessKey))
        {
            return false;
        }

        if (edgeName.Equals(OrbitMeshDefaultNames.DeveloperEdgeName, StringComparison.OrdinalIgnoreCase))
        {
            return CanAccess(caller, accessType) && caller.Scopes.Contains(OrbitMeshScope.Developer);
        }

        var edge = config.Edges.FirstOrDefault(s => s.Name.Equals(edgeName, StringComparison.OrdinalIgnoreCase));
        if (edge == null || string.IsNullOrEmpty(edge.Credential))
        {
            return false;
        }

        var expectedCredential = edge.Credential;
        if (!packageName.Equals(OrbitMeshDefaultNames.EdgePackageName, StringComparison.OrdinalIgnoreCase))
        {
            var package = edge.Packages.FirstOrDefault(p => p.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase));
            if (package == null)
            {
                return false;
            }
            if (!string.IsNullOrEmpty(package.Credential))
            {
                expectedCredential = package.Credential;
            }
        }

        return caller.Name.Equals(expectedCredential, StringComparison.OrdinalIgnoreCase) && CanAccess(caller, accessType);
    }

    // The credential an AccessKey (or session pseudo key) belongs to, if it is enabled. Everything below -
    // access checks, scope checks, authorization rules - goes through here, so a key is verified once and
    // then looked up.
    private CredentialOptions? ResolveEnabled(string? accessKey)
    {
        if (string.IsNullOrEmpty(accessKey))
        {
            return null;
        }
        if (CallerKeys.IsSessionKey(accessKey))
        {
            return sessions.TryGetById(CallerKeys.SessionIdOf(accessKey)) is { } session ? CredentialForSession(session) : null;
        }

        var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accessKey)));
        if (verifiedKeys.TryGetValue(cacheKey, out var name))
        {
            var cached = Current.Credentials.FirstOrDefault(c => c.Enable && c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (cached != null)
            {
                return cached;
            }
            verifiedKeys.TryRemove(cacheKey, out _);
        }

        var found = Current.Credentials.FirstOrDefault(c => c.Enable && MatchesAccessKey(c, accessKey));
        if (found != null)
        {
            if (verifiedKeys.Count >= MaxVerifiedKeys)
            {
                verifiedKeys.Clear();
            }
            verifiedKeys[cacheKey] = found.Name;
        }
        return found;
    }

    private CredentialOptions? CredentialForSession(SessionInfo session)
    {
        if (!session.IsFederated)
        {
            // A password session is just a proof of identity: what it may do is whatever the account holds
            // right now, so disabling or editing the account takes effect on open sessions immediately.
            return Current.Credentials.FirstOrDefault(c =>
                c.Enable && c.Kind == CredentialKind.Human && c.Name.Equals(session.Name, StringComparison.OrdinalIgnoreCase));
        }

        var oidc = Current.Auth.Oidc;
        if (!oidc.IsUsable)
        {
            return null;
        }
        var scopes = OidcRoleMapper.ScopesFor(oidc, session.Groups);
        return scopes.Count == 0
            ? null
            : new CredentialOptions { Name = session.Name, AccessKey = string.Empty, Kind = CredentialKind.Human, Enable = true, Scopes = scopes };
    }

    public IReadOnlyList<string>? GetFederatedGroups(string? accessKey) =>
        CallerKeys.IsSessionKey(accessKey) && sessions.TryGetById(CallerKeys.SessionIdOf(accessKey), touch: false) is { IsFederated: true } session
            ? session.Groups
            : null;

    // A Human credential has no AccessKey to present any more: people sign in with a password and carry a
    // session (see AuthController). Only a hash from before that (AccessKeyHasher, a PBKDF2 of the SHA-1 the
    // old Console sent as its key) still verifies here, so an old Console or script keeps working until its
    // account's first real sign-in upgrades the hash (PasswordHasher) - after which a Human credential matches
    // no AccessKey at all. Machine credentials are AES-encrypted at rest (AccessKeyCipher,
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
                return FixedTimeEquals(cipher.Decrypt(credential.AccessKey), presentedAccessKey);
            }
            // A decrypt failure (malformed or wrong key) just means "not a match", not a crash.
            catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException)
            {
                return false;
            }
        }
        return FixedTimeEquals(credential.AccessKey, presentedAccessKey);
    }

    // Constant-time for equal-length inputs, so a Machine key can't be recovered byte by byte from
    // response timing (impractical against a 256-bit random key, but free to rule out).
    private static bool FixedTimeEquals(string a, string b) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));

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
        // The bypass below only fires for a "__Response" message, and only if SagaRegistry recognizes
        // scope.SagaId as a request THIS server actually saw go out, addressed back to that request's
        // real sender - scope.IsSaga/SagaId/Args are otherwise entirely client-controlled, so trusting
        // them unconditionally let any credential holding messages:execute reach any target (CodeQL
        // cs/user-controlled-bypass).
        if (scope.IsSaga && messageKey == OrbitMeshDefaultNames.SagaResponseMessageKey)
        {
            return sagaRegistry.TryConsumeResponse(scope.SagaId!, scope);
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
        ResolveEnabled(accessKey) is { } credential && predicate(credential);

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

    public CredentialOptions? FindCredential(string? accessKey) => ResolveEnabled(accessKey);

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
