using Microsoft.AspNetCore.Authorization;
using OrbitMesh.Server.Services;

namespace OrbitMesh.Server.Security;

/// <summary>Applies <see cref="IOrbitMeshDirectory.CheckAccess"/> - the one place the door rules live - to the request:
/// the EdgeName/PackageName it presents (header or query string) and the key it authenticated with.</summary>
public sealed class DoorHandler(IOrbitMeshDirectory directory, IHttpContextAccessor accessor) : AuthorizationHandler<DoorRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, DoorRequirement requirement)
    {
        var http = context.Resource as HttpContext ?? accessor.HttpContext;
        var (edgeName, packageName) = http == null ? (null, null)
            : requirement.FixedName != null ? (requirement.FixedName, requirement.FixedName)
            : DoorIdentity.Resolve(http, requirement.AccessType);
        var key = http?.GetEffectiveAccessKey();
        if (http != null
            && directory.CheckAccess(edgeName, packageName, key, requirement.AccessType)
            && (requirement.RequiredScope == null || directory.FindCredential(key)?.Scopes.Contains(requirement.RequiredScope) == true))
        {
            http.GetRequestState().DoorPassed = true;
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}

/// <summary>See <see cref="PackageDownloadRequirement"/>. Both ways in reuse the door rules of <c>CheckAccess</c>; this only
/// says which of its doors a download may come through.</summary>
public sealed class PackageDownloadHandler(IOrbitMeshDirectory directory, IHttpContextAccessor accessor) : AuthorizationHandler<PackageDownloadRequirement>
{
    private static readonly string[] ReservedEdgeNames =
    [
        OrbitMeshDefaultNames.ConsumerEdgeName, OrbitMeshDefaultNames.ControllerEdgeName,
        OrbitMeshDefaultNames.ManagementEdgeName, OrbitMeshDefaultNames.DeveloperEdgeName
    ];

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PackageDownloadRequirement requirement)
    {
        var http = context.Resource as HttpContext ?? accessor.HttpContext;
        if (http == null)
        {
            return Task.CompletedTask;
        }
        // A key and no names at all is a script: it reaches the packages through the Management door (an Edge always names itself).
        var (edgeName, packageName) = DoorIdentity.Resolve(http, OrbitMeshAccessType.OrbitMesh, doorWhenUnnamed: OrbitMeshDefaultNames.ManagementEdgeName);
        var key = http.GetEffectiveAccessKey();
        var credential = directory.FindCredential(key);

        // An Edge fetching its own packages: a declared Edge (never one of the reserved door names), identifying as the
        // Edge itself, with the credential assigned to it and the right to connect as an Edge.
        var asEdge = !string.IsNullOrEmpty(edgeName)
            && !ReservedEdgeNames.Contains(edgeName, StringComparer.OrdinalIgnoreCase)
            && string.Equals(packageName, OrbitMeshDefaultNames.EdgePackageName, StringComparison.OrdinalIgnoreCase)
            && directory.EdgeExists(edgeName)
            && credential?.Scopes.Contains(OrbitMeshScope.EdgeConnect) == true
            && directory.CheckAccess(edgeName, packageName, key, OrbitMeshAccessType.OrbitMesh);

        // Or a person or script let in at the Management door, allowed to read packages.
        var asManager = !asEdge
            && credential?.Scopes.Contains(OrbitMeshScope.PackagesRead) == true
            && directory.CheckAccess(edgeName, packageName, key, OrbitMeshAccessType.Management);

        if (asEdge || asManager)
        {
            http.GetRequestState().DoorPassed = true;
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}

/// <summary>The scopes ride on the identity as <c>scope</c> claims (see <see cref="OrbitMeshClaims"/>), read live from the
/// credential on every request, so a scope removed from an account applies at once.</summary>
public sealed class ScopeHandler : AuthorizationHandler<ScopeRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ScopeRequirement requirement)
    {
        if (context.User.HasClaim(c => c.Type == OrbitMeshClaims.Scope && requirement.AnyOf.Contains(c.Value)))
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail(new AuthorizationFailureReason(this, $"Forbidden: this action requires the '{string.Join("' or '", requirement.AnyOf)}' scope."));
        }
        return Task.CompletedTask;
    }
}

/// <summary>Turns <c>[RequiresScope(...)]</c>'s policy names (<c>scope:a|b</c>) into policies on demand.</summary>
public sealed class OrbitMeshPolicyProvider(Microsoft.Extensions.Options.IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, AuthorizationPolicy> scopePolicies = new();

    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (OrbitMeshPolicies.TryParseScopes(policyName, out var anyOf))
        {
            return Task.FromResult<AuthorizationPolicy?>(scopePolicies.GetOrAdd(policyName,
                _ => new AuthorizationPolicyBuilder().AddRequirements(new ScopeRequirement(anyOf)).Build()));
        }
        return base.GetPolicyAsync(policyName);
    }
}
