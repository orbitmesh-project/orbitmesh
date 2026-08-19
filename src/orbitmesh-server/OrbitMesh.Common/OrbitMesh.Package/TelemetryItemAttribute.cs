namespace OrbitMesh.Package;

/// <summary>Marks a class as a telemetry payload type, for documentation/tooling purposes (e.g.
/// <see cref="PackageHost.DescribeTelemetryItemTypesFromAssembly"/>) - it does not change how the type
/// is serialized. Use with <see cref="PackageHost.PushTelemetryItem{TTelemetryItem}"/>.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class TelemetryItemAttribute : Attribute
{
}
