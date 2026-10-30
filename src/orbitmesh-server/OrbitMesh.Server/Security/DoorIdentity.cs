namespace OrbitMesh.Server.Security;

/// <summary>
/// The EdgeName and PackageName a request is judged by. They are what an Edge or a package says it is - needed to tell
/// which Edge's door it knocks on - but for a script calling the Management, Controller or Consumer API with a key they
/// are noise: the endpoint already says which door it is. So when a caller presents a key and names neither, they are
/// filled in from the door the endpoint belongs to, which lets <c>curl -H "Authorization: Bearer omk_..." .../rest/management/edges</c>
/// work. Nothing is widened by it: the caller could have sent the same names itself. Cookie sessions don't get it (their
/// requests keep the explicit identity headers, see the cookie rule in <see cref="OrbitMeshPolicyEvaluator"/>), and the
/// Edge and package doors never do - who an Edge is has to be said.
/// </summary>
public static class DoorIdentity
{
    public const string DefaultPackageName = "api";

    public static (string? EdgeName, string? PackageName) Resolve(HttpContext http, OrbitMeshAccessType accessType, string? doorWhenUnnamed = null)
    {
        var edge = http.Request.GetHeaderOrQuery(OrbitMeshHeaderNames.EdgeName);
        var package = http.Request.GetHeaderOrQuery(OrbitMeshHeaderNames.PackageName);
        if (string.IsNullOrEmpty(edge) && string.IsNullOrEmpty(package) && http.GetRequestState().PresentedAccessKey)
        {
            var door = doorWhenUnnamed ?? DoorOf(accessType, http.Request.Path);
            if (door != null)
            {
                return (door, DefaultPackageName);
            }
        }
        return (edge, package);
    }

    private static string? DoorOf(OrbitMeshAccessType accessType, PathString path) => accessType switch
    {
        OrbitMeshAccessType.Management => OrbitMeshDefaultNames.ManagementEdgeName,
        OrbitMeshAccessType.Controller => OrbitMeshDefaultNames.ControllerEdgeName,
        OrbitMeshAccessType.OrbitMesh when path.StartsWithSegments("/rest/consumer", StringComparison.OrdinalIgnoreCase) => OrbitMeshDefaultNames.ConsumerEdgeName,
        _ => null
    };
}
