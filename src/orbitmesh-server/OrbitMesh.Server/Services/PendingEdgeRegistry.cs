using System.Collections.Concurrent;

namespace OrbitMesh.Server.Services;

public sealed record PendingEdgeInfo(string InstanceId, string EdgeName, string RemoteIp, string ConnectionId, DateTime FirstSeenUtc, DateTime LastSeenUtc);

/// <summary>Tracks Edge connection attempts with a valid InstanceId+EdgeName that failed CheckAccess.
/// In-memory only (like LoginAttemptLimiter) - a restart clears it, which is fine since a still-trying
/// Edge reappears within seconds. ConnectionId lets an approval push the new credential straight down
/// the still-open connection (see EdgeClientMethodNames.EdgeApproved).</summary>
public interface IPendingEdgeRegistry
{
    List<PendingEdgeInfo> GetAll();

    PendingEdgeInfo? Get(string instanceId);

    void RecordAttempt(string instanceId, string edgeName, string remoteIp, string connectionId);

    void Remove(string instanceId);
}

public sealed class PendingEdgeRegistry : IPendingEdgeRegistry
{
    private readonly ConcurrentDictionary<string, PendingEdgeInfo> pending = new();

    public List<PendingEdgeInfo> GetAll() => pending.Values.OrderBy(p => p.FirstSeenUtc).ToList();

    public PendingEdgeInfo? Get(string instanceId) => pending.GetValueOrDefault(instanceId);

    public void RecordAttempt(string instanceId, string edgeName, string remoteIp, string connectionId)
    {
        var now = DateTime.UtcNow;
        pending.AddOrUpdate(
            instanceId,
            _ => new PendingEdgeInfo(instanceId, edgeName, remoteIp, connectionId, now, now),
            (_, existing) => existing with { EdgeName = edgeName, RemoteIp = remoteIp, ConnectionId = connectionId, LastSeenUtc = now });
    }

    public void Remove(string instanceId) => pending.TryRemove(instanceId, out _);
}
