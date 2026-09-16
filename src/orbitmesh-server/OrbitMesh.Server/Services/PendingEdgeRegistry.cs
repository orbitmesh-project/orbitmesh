using System.Collections.Concurrent;

namespace OrbitMesh.Server.Services;

public sealed record PendingEdgeInfo(string InstanceId, string EdgeName, string RemoteIp, DateTime FirstSeenUtc, DateTime LastSeenUtc, string? ApprovedAccessKey = null, DateTime? ApprovedAtUtc = null);

/// <summary>Tracks Edge REST enrollment attempts (<c>rest/enroll</c>) for an InstanceId that has no
/// configured Edge/Credential yet. In-memory only (like LoginAttemptLimiter) - a restart clears it,
/// which is fine since a still-trying Edge re-enrolls within seconds of its next poll.
/// <para>Once approved, the entry is kept (rather than removed) with <see cref="PendingEdgeInfo.ApprovedAccessKey"/>
/// set, so the Edge's next <c>GET rest/enroll/{instanceId}/status</c> poll can pick up the credential -
/// this is the only place the plaintext key is handed over. It's purged either when the Edge
/// reconnects for real over SignalR with the new key (<see cref="Remove"/>, called from
/// EdgeHub.OnConnectedAsync) or after <see cref="ApprovedEntryLifetime"/> elapses, whichever comes
/// first - an unbounded lifetime would make a leaked/guessed InstanceId a standing way to fetch a
/// live credential.</para></summary>
public interface IPendingEdgeRegistry
{
    /// <summary>Still-pending entries only (excludes ones awaiting pickup after approval) - what the
    /// Console's pending-edges list shows.</summary>
    List<PendingEdgeInfo> GetAll();

    /// <summary>Any entry, pending or approved-awaiting-pickup - what a status poll checks.</summary>
    PendingEdgeInfo? Get(string instanceId);

    void RecordAttempt(string instanceId, string edgeName, string remoteIp);

    /// <summary>Refreshes LastSeenUtc without touching anything else - called on a plain status poll
    /// so the Console's pending list reflects that the Edge is still actively retrying, without
    /// re-running RecordAttempt's "is this InstanceId already configured" guard on every poll.</summary>
    void Touch(string instanceId);

    /// <summary>Marks a pending entry approved, keeping it around (with the new key attached) until
    /// picked up - see the type-level remarks.</summary>
    void MarkApproved(string instanceId, string accessKey);

    void Remove(string instanceId);
}

public sealed class PendingEdgeRegistry : IPendingEdgeRegistry
{
    private static readonly TimeSpan ApprovedEntryLifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, PendingEdgeInfo> pending = new();

    public List<PendingEdgeInfo> GetAll()
    {
        PurgeExpiredApprovals();
        return pending.Values.Where(p => p.ApprovedAccessKey == null).OrderBy(p => p.FirstSeenUtc).ToList();
    }

    public PendingEdgeInfo? Get(string instanceId)
    {
        PurgeExpiredApprovals();
        return pending.GetValueOrDefault(instanceId);
    }

    public void RecordAttempt(string instanceId, string edgeName, string remoteIp)
    {
        var now = DateTime.UtcNow;
        pending.AddOrUpdate(
            instanceId,
            _ => new PendingEdgeInfo(instanceId, edgeName, remoteIp, now, now),
            // A late/duplicate enroll POST must not stomp an approval already recorded for this InstanceId.
            (_, existing) => existing.ApprovedAccessKey != null
                ? existing with { LastSeenUtc = now }
                : existing with { EdgeName = edgeName, RemoteIp = remoteIp, LastSeenUtc = now });
    }

    public void Touch(string instanceId)
    {
        if (pending.TryGetValue(instanceId, out var existing))
        {
            pending.TryUpdate(instanceId, existing with { LastSeenUtc = DateTime.UtcNow }, existing);
        }
    }

    public void MarkApproved(string instanceId, string accessKey)
    {
        if (pending.TryGetValue(instanceId, out var existing))
        {
            pending.TryUpdate(instanceId, existing with { ApprovedAccessKey = accessKey, ApprovedAtUtc = DateTime.UtcNow }, existing);
        }
    }

    public void Remove(string instanceId) => pending.TryRemove(instanceId, out _);

    private void PurgeExpiredApprovals()
    {
        var now = DateTime.UtcNow;
        foreach (var (instanceId, info) in pending)
        {
            if (info.ApprovedAtUtc is { } approvedAt && now - approvedAt > ApprovedEntryLifetime)
            {
                pending.TryRemove(instanceId, out _);
            }
        }
    }
}
