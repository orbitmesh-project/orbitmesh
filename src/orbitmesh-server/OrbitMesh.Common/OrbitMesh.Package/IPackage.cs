namespace OrbitMesh.Package;

/// <summary>The three lifecycle hooks every OrbitMesh package implements. Everything else - settings,
/// telemetry, messages, logging - goes through the static <see cref="PackageHost"/> class instead of
/// this interface. Start a package with <see cref="PackageHost.Start{TPackage}"/>.</summary>
public interface IPackage
{
    /// <summary>Called once the package is connected and ready. Kick off background work here.</summary>
    void OnStart();

    /// <summary>Called just before shutdown, before the connection is torn down - stop accepting new
    /// work, but the connection (and thus <see cref="PackageHost"/> calls) is still usable here.</summary>
    void OnPreShutdown();

    /// <summary>Called during shutdown, after the connection is gone. Final local cleanup only.</summary>
    void OnShutdown();
}
