namespace OrbitMesh.Server;

/// <summary>
/// Named permission scopes a credential can hold (see Configuration.CredentialOptions.Scopes). Each
/// scope gates one coherent slice of the Management API/ControlHub surface.
/// Configuration.AuthorizationOptions (Messages/Groups/TelemetryItems rules) still applies underneath as
/// a finer per-target filter once a scope already allows the action class - the same two-level model as
/// AWS IAM (a policy authorizes the action, a resource condition restricts which target it applies to).
/// </summary>
public static class OrbitMeshScope
{
    public const string EdgesRead = "edges:read";
    public const string EdgesWrite = "edges:write";
    public const string PackagesRead = "packages:read";
    public const string PackagesControl = "packages:control";
    public const string PackagesManage = "packages:manage";
    public const string PackagesDeploy = "packages:deploy";
    public const string TelemetryRead = "telemetry:read";
    public const string TelemetryPurge = "telemetry:purge";
    public const string CredentialsRead = "credentials:read";
    public const string CredentialsManage = "credentials:manage";
    public const string ConfigurationRead = "configuration:read";
    public const string ConfigurationWrite = "configuration:write";
    public const string UpdatesManage = "updates:manage";
    public const string Developer = "developer";

    public static readonly IReadOnlyList<string> All =
    [
        EdgesRead, EdgesWrite,
        PackagesRead, PackagesControl, PackagesManage, PackagesDeploy,
        TelemetryRead, TelemetryPurge,
        CredentialsRead, CredentialsManage,
        ConfigurationRead, ConfigurationWrite,
        UpdatesManage,
        Developer
    ];
}
