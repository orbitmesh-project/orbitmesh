namespace OrbitMesh.Package;

/// <summary>Marks a property as a live mirror of another package's telemetry item - register with
/// <see cref="PackageHost.RegisterTelemetryItemLinks(object?)"/> once and the property is kept in sync
/// automatically as updates arrive, instead of subscribing imperatively via
/// <see cref="PackageHost.RegisterTelemetryItemCallback"/>.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TelemetryItemLinkAttribute : Attribute
{
    /// <summary>Source edge name, or "*" for any edge.</summary>
    public string Edge { get; set; }

    /// <summary>Source package name, or "*" for any package.</summary>
    public string Package { get; set; }

    /// <summary>Telemetry item name to mirror, or "*" for any name.</summary>
    public string Name { get; set; }

    /// <summary>Telemetry item type to mirror, or "*" for any type.</summary>
    public string Type { get; set; }

    /// <summary>If true (the default), the property is populated with the item's current value as
    /// soon as the link is registered, rather than waiting for the next update.</summary>
    public bool RequestValueOnInit { get; set; }

    /// <summary>Matches any edge/package/name/type - narrow this down in practice, this default is
    /// rarely what you want.</summary>
    public TelemetryItemLinkAttribute()
        : this("*", "*", "*", "*")
    {
    }

    /// <summary>Matches a specific package/name on any edge.</summary>
    public TelemetryItemLinkAttribute(string package, string name)
        : this("*", package, name, "*")
    {
    }

    /// <summary>Matches a specific edge/package/name.</summary>
    public TelemetryItemLinkAttribute(string edge, string package, string name)
        : this(edge, package, name, "*")
    {
    }

    /// <summary>Matches a specific edge/package/name/type.</summary>
    public TelemetryItemLinkAttribute(string edge, string package, string name, string type)
    {
        RequestValueOnInit = true;
        Edge = edge;
        Package = package;
        Name = name;
        Type = type;
    }
}
