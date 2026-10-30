using Microsoft.AspNetCore.Authorization;
using OrbitMesh.Server.Security;

namespace OrbitMesh.Server.Services;

/// <summary>
/// Per-endpoint authorization for the Management API's actions - the caller must hold at least one of the listed
/// scopes (see OrbitMeshScope). It is an ordinary <see cref="AuthorizeAttribute"/> whose policy is "one of these
/// scopes": the controller's own door policy decides whether the request reaches the controller at all (is this
/// credential Management-capable, is this session allowed on this door), this attribute decides whether it reaches
/// this specific action.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresScopeAttribute : AuthorizeAttribute
{
    public RequiresScopeAttribute(params string[] anyOf) => Policy = OrbitMeshPolicies.ForScopes(anyOf);
}
