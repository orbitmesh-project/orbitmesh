using System.Collections.Concurrent;

namespace OrbitMesh.Server.Services;

/// <summary>
/// In-memory "last used" timestamps per credential, flushed to appsettings.json on a delay by
/// <see cref="CredentialUsageFlushService"/> rather than on every request - a successful auth check
/// happens on essentially every REST call and SignalR connect, and writing to disk (a full
/// read-mutate-write of appsettings.json under a lock, see IOrbitMeshConfigWriter) that often would
/// turn routine traffic into a disk-I/O bottleneck and a hot spot for write contention.
/// </summary>
public sealed class CredentialUsageTracker
{
    private readonly ConcurrentDictionary<string, DateTime> lastUsed = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> dirty = new(StringComparer.OrdinalIgnoreCase);

    public void RecordUse(string? credentialName)
    {
        if (string.IsNullOrEmpty(credentialName))
        {
            return;
        }
        lastUsed[credentialName] = DateTime.UtcNow;
        dirty[credentialName] = 0;
    }

    public DateTime? GetLastUsed(string credentialName) =>
        lastUsed.TryGetValue(credentialName, out var when) ? when : null;

    /// <summary>Names touched since the last flush, and clears the dirty set - call once per flush pass.</summary>
    public List<string> TakeDirtyNames()
    {
        var names = dirty.Keys.ToList();
        foreach (var name in names)
        {
            dirty.TryRemove(name, out _);
        }
        return names;
    }
}
