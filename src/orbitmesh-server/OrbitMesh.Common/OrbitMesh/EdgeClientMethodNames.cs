namespace OrbitMesh;

/// <summary>Method names the server invokes on EdgeHub clients (the Edge process) - internal wire protocol.</summary>
public static class EdgeClientMethodNames
{
    /// <summary>Pushes the Edge's current package assignment list.</summary>
    public const string PushPackagesList = "PushPackagesList";
    /// <summary>A remote control action (start/stop/restart) for a package this Edge hosts.</summary>
    public const string PackageControlAction = "PackageControlAction";
    /// <summary>Carries the new AccessKey when an admin approves a pending Edge (see IPendingEdgeRegistry).</summary>
    public const string EdgeApproved = "EdgeApproved";
}
