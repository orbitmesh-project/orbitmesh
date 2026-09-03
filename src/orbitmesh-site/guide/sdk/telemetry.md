# Telemetry

Telemetry items are how a package publishes state for the Console, or other packages, to see:

```csharp
PackageHost.PushTelemetryItem("SunInfo", new SunInfo { Sunrise = ..., Sunset = ... });
```

`[TelemetryItem]` marks a type as a telemetry payload - documentation/tooling only, it doesn't change serialization.

Every telemetry item pushed shows up automatically on the Console's Telemetry page - nothing to register or configure on the Console side:

![Telemetry page listing items pushed by connected packages](/screenshots/telemetry.jpg)

## Consuming another package's telemetry

Use `[TelemetryItemLink]` on a property and call `PackageHost.RegisterTelemetryItemLinks(this)` once. The field stays in sync automatically:

```csharp
[TelemetryItemLink("Weather", "CurrentTemperature")]
public double? OutsideTemperature { get; set; }
```

Or subscribe imperatively instead of via a property:

```csharp
PackageHost.RegisterTelemetryItemCallback(item => { /* ... */ }, package: "Weather", name: "CurrentTemperature");
```
