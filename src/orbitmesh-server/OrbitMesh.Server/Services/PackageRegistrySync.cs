using OrbitMesh.Server.Models;

namespace OrbitMesh.Server.Services;

/// <summary>
/// Seeds <see cref="IPackageRegistry"/> from the configured edges/packages so the admin console sees
/// declared-but-not-yet-connected packages (was <c>ControlHub.LoadPackagesFromConfiguration</c> in the
/// original, run once at hub init and again on every config reload).
/// </summary>
public sealed class PackageRegistrySync(IOrbitMeshDirectory directory, IPackageRegistry packageRegistry)
{
    public void Sync()
    {
        var validIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in directory.Current.Edges)
        {
            foreach (var package in directory.GetPackagesList(edge.Name))
            {
                var packageInstanceId = directory.GetPackageInstanceId(edge.Name, package.Name);
                validIds.Add(packageInstanceId);
                packageRegistry.GetOrAdd(packageInstanceId, () => new PackageInfo
                {
                    Package = package,
                    State = PackageState.Unknown,
                    LastUpdate = DateTime.MinValue
                });
            }
        }
        packageRegistry.PurgeMissing(validIds);
    }
}
