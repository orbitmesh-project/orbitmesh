namespace OrbitMesh.Package;

/// <summary>Declares the concrete types a polymorphic telemetry item type can actually be, so
/// deserialization on the receiving side can pick the right one.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class TelemetryItemKnownTypesAttribute : Attribute
{
    /// <summary>The possible concrete types.</summary>
    public Type[] TelemetryItemKnownTypes { get; set; }

    /// <summary>Declares the possible concrete types.</summary>
    public TelemetryItemKnownTypesAttribute(params Type[] knownType)
    {
        TelemetryItemKnownTypes = knownType;
    }
}
