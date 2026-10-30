using Microsoft.AspNetCore.Authorization;

namespace OrbitMesh.Server.Security;

/// <summary>The names of the authorization policies, and the requirements behind them. A controller declares the door
/// it belongs to (<c>[Authorize(Policy = OrbitMeshPolicies.ManagementDoor)]</c>), an action the scope it needs
/// (<c>[RequiresScope(...)]</c>), and anything left undeclared is refused by the fallback policy: public endpoints
/// are the ones that say <c>[AllowAnonymous]</c>, not the ones missing from a list.</summary>
public static class OrbitMeshPolicies
{
    public const string ManagementDoor = "door:management";
    public const string ControllerDoor = "door:controller";
    public const string OrbitMeshDoor = "door:orbitmesh";

    /// <summary>Downloading a package's zip from <c>/packages/...</c>.</summary>
    public const string PackageDownload = "door:package-download";

    // The SignalR hubs, authorized when the connection is negotiated: a refused caller never gets a connection.
    public const string ConsumerHub = "hub:consumer";
    public const string ControlHub = "hub:control";
    public const string EdgeHub = "hub:edge";
    public const string PackageHub = "hub:package";

    private const string ScopePrefix = "scope:";
    private const char ScopeSeparator = '|';

    /// <summary>The policy that needs one of <paramref name="anyOf"/>: scope names never contain '|'.</summary>
    public static string ForScopes(IEnumerable<string> anyOf) => ScopePrefix + string.Join(ScopeSeparator, anyOf);

    public static bool TryParseScopes(string policyName, out string[] anyOf)
    {
        if (!policyName.StartsWith(ScopePrefix, StringComparison.Ordinal))
        {
            anyOf = [];
            return false;
        }
        anyOf = policyName[ScopePrefix.Length..].Split(ScopeSeparator, StringSplitOptions.RemoveEmptyEntries);
        return true;
    }

    public static AuthorizationPolicy Door(OrbitMeshAccessType accessType, string? fixedName = null, string? requiredScope = null) =>
        new AuthorizationPolicyBuilder().AddRequirements(new DoorRequirement(accessType, fixedName, requiredScope)).Build();
}

/// <summary>A requirement about <i>getting in</i> (as opposed to what you may do once in): its refusal is a refusal of the
/// caller, which is what counts toward the IP lockout, whereas a missing scope is not.</summary>
public interface IDoorRequirement : IAuthorizationRequirement
{
    OrbitMeshAccessType AccessType { get; }
}

/// <summary>The caller must be let in through this kind of door: the rules of <c>IOrbitMeshDirectory.CheckAccess</c>
/// (which credential may use which EdgeName, whether it holds a scope at all, sessions only on the Console's doors...).</summary>
public sealed class DoorRequirement(OrbitMeshAccessType accessType, string? fixedName = null, string? requiredScope = null) : IDoorRequirement
{
    public OrbitMeshAccessType AccessType { get; } = accessType;

    /// <summary>A door that is judged by this name (as EdgeName and PackageName) whatever the caller says - the Consumer hub.</summary>
    public string? FixedName { get; } = fixedName;

    /// <summary>A scope the caller must hold just to be let in (an Edge's or a package's connection scope), on top of the door's own rules.</summary>
    public string? RequiredScope { get; } = requiredScope;
}

/// <summary>
/// Who may download a package's zip: an Edge, with its own credential (EdgeName is that Edge, PackageName is "Edge" - what
/// both Edges send), or someone let in at the Management door who holds <c>packages:read</c>. Not any credential that
/// can name the Consumer door, which used to be enough.
/// </summary>
public sealed class PackageDownloadRequirement : IDoorRequirement
{
    public OrbitMeshAccessType AccessType => OrbitMeshAccessType.OrbitMesh;
}

/// <summary>The caller must hold at least one of these scopes.</summary>
public sealed class ScopeRequirement(string[] anyOf) : IAuthorizationRequirement
{
    public string[] AnyOf { get; } = anyOf;
}
