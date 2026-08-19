# Getting started

A package is a normal .NET console app that references `OrbitMesh.Common` and implements `IPackage`. `PackageHost.Start<T>` owns the connection lifecycle - your code just reacts to it.

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

`IPackage` is three methods: `OnStart`, `OnPreShutdown`, `OnShutdown`. Everything else - settings, telemetry, messages, logging - goes through the static `PackageHost` class.

- [Settings](/guide/sdk/settings)
- [Telemetry](/guide/sdk/telemetry)
- [Messages (RPC)](/guide/sdk/messages)
- [Logging](/guide/sdk/logging)
- [PackageInfo.xml](/guide/sdk/manifest)
- [Packaging and distribution](/guide/sdk/packaging)
