using OrbitMesh.Server.Models;

namespace OrbitMesh.Server.Services;

/// <summary>
/// Tracks live package connection/state info (was <c>ControlHub.PackagesInfos</c>) and declared
/// <see cref="PackageDescriptor"/>s (was <c>OrbitMeshHub.PackageDescriptors</c>), as a singleton
/// service instead of hub-static state.
/// </summary>
public interface IPackageRegistry
{
    IReadOnlyDictionary<string, PackageInfo> PackagesInfos { get; }

    IReadOnlyDictionary<string, PackageDescriptor> PackageDescriptors { get; }

    PackageInfo GetOrAdd(string packageInstanceId, Func<PackageInfo> factory);

    bool TryGetPackageInfo(string packageInstanceId, out PackageInfo info);

    void Remove(string packageInstanceId);

    /// <summary>Drops entries that are neither in <paramref name="validPackageInstanceIds"/> nor currently connected.</summary>
    void PurgeMissing(IReadOnlyCollection<string> validPackageInstanceIds);

    void SetDescriptor(string packageName, PackageDescriptor descriptor);

    void PurgeUnusedDescriptors(IReadOnlyCollection<string> knownPackageNames);
}

public sealed class PackageRegistry : IPackageRegistry
{
    private readonly Dictionary<string, PackageInfo> _packagesInfos = new();
    private readonly Dictionary<string, PackageDescriptor> _descriptors = new();
    private readonly object _lock = new();

    public IReadOnlyDictionary<string, PackageInfo> PackagesInfos
    {
        get { lock (_lock) { return new Dictionary<string, PackageInfo>(_packagesInfos); } }
    }

    public IReadOnlyDictionary<string, PackageDescriptor> PackageDescriptors
    {
        get { lock (_lock) { return new Dictionary<string, PackageDescriptor>(_descriptors); } }
    }

    public PackageInfo GetOrAdd(string packageInstanceId, Func<PackageInfo> factory)
    {
        lock (_lock)
        {
            if (!_packagesInfos.TryGetValue(packageInstanceId, out var info))
            {
                info = factory();
                _packagesInfos[packageInstanceId] = info;
            }
            return info;
        }
    }

    public bool TryGetPackageInfo(string packageInstanceId, out PackageInfo info)
    {
        lock (_lock)
        {
            return _packagesInfos.TryGetValue(packageInstanceId, out info!);
        }
    }

    public void Remove(string packageInstanceId)
    {
        lock (_lock)
        {
            _packagesInfos.Remove(packageInstanceId);
        }
    }

    public void PurgeMissing(IReadOnlyCollection<string> validPackageInstanceIds)
    {
        lock (_lock)
        {
            foreach (var key in _packagesInfos.Where(p => !validPackageInstanceIds.Contains(p.Key, StringComparer.OrdinalIgnoreCase) && !p.Value.IsConnected).Select(p => p.Key).ToList())
            {
                _packagesInfos.Remove(key);
            }
        }
    }

    public void SetDescriptor(string packageName, PackageDescriptor descriptor)
    {
        lock (_lock)
        {
            _descriptors[packageName] = descriptor;
        }
    }

    public void PurgeUnusedDescriptors(IReadOnlyCollection<string> knownPackageNames)
    {
        lock (_lock)
        {
            foreach (var key in _descriptors.Keys.Where(k => !knownPackageNames.Contains(k)).ToList())
            {
                _descriptors.Remove(key);
            }
        }
    }
}
