using OrbitMesh.Server.Configuration;

namespace OrbitMesh.Server.Services;

public static class OidcRoleMapper
{
    public const string SessionNamePrefix = "oidc:";

    public const string AllScopes = "*";

    /// <summary>The scopes granted to someone in <paramref name="groups"/>: the union over every matching
    /// mapping, unknown scope names dropped. Empty means "not allowed to sign in".</summary>
    public static List<string> ScopesFor(OidcOptions oidc, IEnumerable<string> groups)
    {
        var memberOf = groups.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Expand(oidc.RoleMappings.Where(m => memberOf.Contains(m.Group)).SelectMany(m => m.Scopes));
    }

    /// <summary>Resolves "*" and drops anything that isn't a scope a person can hold: unknown names, and the
    /// Edge/package connection scopes (<see cref="OrbitMeshScope.MachineOnly"/>), which a signed-in person's
    /// session can never use - so "*" is every scope an administrator account has.</summary>
    public static List<string> Expand(IEnumerable<string> scopes)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scope in scopes)
        {
            if (scope == AllScopes)
            {
                result.UnionWith(OrbitMeshScope.ForPeople);
            }
            else if (OrbitMeshScope.ForPeople.Contains(scope))
            {
                result.Add(scope);
            }
        }
        return result.ToList();
    }
}
