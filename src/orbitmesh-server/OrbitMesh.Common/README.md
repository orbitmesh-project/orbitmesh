# OrbitMesh.Common

The OrbitMesh Package SDK (`PackageHost`) and shared models - the .NET client library for writing
packages that run on an [OrbitMesh](https://github.com/orbitmesh-project/orbitmesh) Edge and talk to an
OrbitMesh Server: settings, telemetry, messages (RPC between packages) and lifecycle management.

## Quick start

A package is a normal .NET console app that references this package and implements `IPackage`.
`PackageHost.Start<T>` owns the whole connection lifecycle - your code just reacts to it.

```csharp
using OrbitMesh.Package;

public static class Program
{
    private static void Main(string[] args) => PackageHost.Start<DayInfoPackage>(args);
}

public sealed class DayInfoPackage : IPackage
{
    public void OnStart()
    {
        // Runs once the package is up. Kick off background work here.
    }

    public void OnPreShutdown()
    {
        // Signalled just before shutdown - stop accepting new work.
    }

    public void OnShutdown()
    {
        // Final cleanup.
    }

    [MessageHandler(Description = "Example RPC-style call exposed to other packages/the Console.")]
    public string Ping() => "pong";
}
```

`IPackage` is intentionally three methods: `OnStart`, `OnPreShutdown`, `OnShutdown`. Everything else -
settings, telemetry, messages, logging - goes through the static `PackageHost` class.

## Settings

Declared in your package's `PackageInfo.xml` manifest, read at runtime:

```csharp
int timezone = PackageHost.GetSettingValue<int>("TimeZone");
bool has = PackageHost.ContainsSetting("ApiKey");
PackageHost.TryGetSettingValue<double>("Latitude", out var lat, defaultValue: 0);

// A JsonObject-typed setting:
var config = PackageHost.GetSettingAsJson<MyConfig>("OpenWeatherConfiguration");
```

`SettingsUpdated` fires whenever the Console pushes a settings change while the package is running -
subscribe to it instead of only reading settings once in `OnStart` if a live setting (e.g. an API key)
needs to take effect without a restart.

## Telemetry

Telemetry items are how a package publishes state for the Console/other packages to see:

```csharp
PackageHost.PushTelemetryItem("SunInfo", new SunInfo { Sunrise = ..., Sunset = ... });
```

`[TelemetryItem]` marks a type as a telemetry payload. To *consume* another package's telemetry
instead of publishing your own, use `[TelemetryItemLink]` on a property and call
`PackageHost.RegisterTelemetryItemLinks(this)` once - the property is kept in sync automatically:

```csharp
[TelemetryItemLink("Weather", "CurrentTemperature")]
public double? OutsideTemperature { get; set; }
```

## Messages (RPC between packages)

`[MessageHandler]` exposes a method other packages (or the Console) can call by key:

```csharp
[MessageHandler("GetSunInfo", Description = "Calculates sunrise/sunset for a date and location.")]
public SunInfo GetSunInfo(DateOnly date, int timezone, double latitude, double longitude) { /* ... */ }
```

Call `PackageHost.RegisterMessageHandlers(this)` once (typically in `OnStart`) to wire up every
`[MessageHandler]` method on an instance. From the caller's side:

```csharp
var result = await PackageHost.SendMessageAsync<SunInfo>(MessageScope.Create("DayInfo"), "GetSunInfo", new { date, timezone, latitude, longitude });
```

## Logging

```csharp
PackageHost.WriteInfo("Processed {0} items", count);
PackageHost.WriteWarn(...);
PackageHost.WriteError(...);
PackageHost.WriteDebug(...);
```

These show up in the Console's Console log page, scoped to the Edge/package that wrote them.

## Learn more

Full documentation - architecture, deployment, and the complete `PackageInfo.xml` manifest reference -
lives at the [OrbitMesh site](https://github.com/orbitmesh-project/orbitmesh).
