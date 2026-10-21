using System.Security.Cryptography;

namespace OrbitMesh.Server.Services;

/// <summary>What the Console's pending-edges list shows - deliberately carries neither the poll-secret
/// hash nor an approved key.</summary>
public sealed record PendingEdgeInfo(string InstanceId, string EdgeName, string MachineName, string Platform, string RemoteIp, DateTime FirstSeenUtc, DateTime LastSeenUtc);

/// <summary>Result of an enroll/status call against <see cref="IPendingEdgeRegistry"/>.</summary>
public enum EnrollOutcome
{
    /// <summary>Recorded and waiting on an admin.</summary>
    Pending,
    /// <summary>Approved - <see cref="EnrollResult.AccessKey"/> is set.</summary>
    Approved,
    /// <summary>No matching entry for this InstanceId + secret.</summary>
    Unknown,
    /// <summary>Entry exists but was created with a different poll secret - not overwritten.</summary>
    SecretMismatch,
    /// <summary>The registry is full (global or per-IP cap) - nothing was recorded.</summary>
    TooMany
}

public readonly record struct EnrollResult(EnrollOutcome Outcome, string? AccessKey = null, string? EdgeName = null);

/// <summary>Tracks Edge REST enrollment attempts (<c>rest/enroll</c>) for an InstanceId that has no
/// configured Edge/Credential yet. In-memory only (like LoginAttemptLimiter) - a restart clears it,
/// and a still-trying Edge simply re-enrolls on its next poll.
/// <para>Every entry is bound to a poll secret the Edge generated and persisted locally; only its
/// SHA-256 is kept here. The InstanceId alone (visible to any <c>edges:read</c> user in the pending
/// list) is therefore not enough to read a status or collect a key, nor to hijack an existing entry.</para>
/// <para>Bounded on every axis an unauthenticated caller controls: total entries, entries per source
/// IP, and lifetime (a pending entry expires if its Edge stops polling; an approved one is purged
/// once picked up via a real reconnect - <see cref="Remove"/>, called from EdgeHub.OnConnectedAsync -
/// or after <see cref="ApprovedEntryLifetime"/>, so a plaintext key never lingers).</para></summary>
public interface IPendingEdgeRegistry
{
    /// <summary>Still-pending entries only - what the Console's list shows.</summary>
    List<PendingEdgeInfo> GetAll();

    /// <summary>A still-pending (not yet approved) entry, for the approval endpoint.</summary>
    PendingEdgeInfo? GetPending(string instanceId);

    /// <summary>Records or refreshes an enrollment attempt. A different <paramref name="secretHash"/>
    /// than the one already on file never overwrites it.</summary>
    EnrollResult Enroll(string instanceId, string edgeName, string machineName, string platform, string remoteIp, byte[] secretHash);

    /// <summary>Status poll: refreshes LastSeenUtc and returns the key once approved.</summary>
    EnrollResult GetStatus(string instanceId, byte[] secretHash);

    /// <summary>Marks the entry approved under <paramref name="edgeName"/> (the name the admin chose, not
    /// necessarily the one the Edge declared), keeping the new key for the Edge's next status poll.</summary>
    void MarkApproved(string instanceId, string accessKey, string edgeName);

    void Remove(string instanceId);
}

public sealed class PendingEdgeRegistry : IPendingEdgeRegistry
{
    private const int MaxEntries = 100;
    private const int MaxEntriesPerIp = 5;
    private static readonly TimeSpan ApprovedEntryLifetime = TimeSpan.FromMinutes(10);
    // The Edge polls every 5s, so a live pending Edge refreshes this constantly.
    private static readonly TimeSpan PendingIdleLifetime = TimeSpan.FromMinutes(15);

    private sealed record Entry(
        string InstanceId, string EdgeName, string MachineName, string Platform, string RemoteIp,
        DateTime FirstSeenUtc, DateTime LastSeenUtc, byte[] SecretHash,
        string? ApprovedAccessKey = null, DateTime? ApprovedAtUtc = null, string? ApprovedEdgeName = null);

    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);

    public List<PendingEdgeInfo> GetAll()
    {
        lock (gate)
        {
            Purge();
            return entries.Values
                .Where(e => e.ApprovedAccessKey == null)
                .OrderBy(e => e.FirstSeenUtc)
                .Select(ToInfo)
                .ToList();
        }
    }

    public PendingEdgeInfo? GetPending(string instanceId)
    {
        lock (gate)
        {
            Purge();
            return entries.TryGetValue(instanceId, out var entry) && entry.ApprovedAccessKey == null ? ToInfo(entry) : null;
        }
    }

    public EnrollResult Enroll(string instanceId, string edgeName, string machineName, string platform, string remoteIp, byte[] secretHash)
    {
        lock (gate)
        {
            Purge();
            var now = DateTime.UtcNow;
            if (entries.TryGetValue(instanceId, out var existing))
            {
                if (!CryptographicOperations.FixedTimeEquals(existing.SecretHash, secretHash))
                {
                    return new EnrollResult(EnrollOutcome.SecretMismatch);
                }
                // A late/duplicate enroll must not stomp an approval already recorded.
                entries[instanceId] = existing.ApprovedAccessKey != null
                    ? existing with { LastSeenUtc = now }
                    : existing with { EdgeName = edgeName, MachineName = machineName, Platform = platform, RemoteIp = remoteIp, LastSeenUtc = now };
                return ToResult(entries[instanceId]);
            }

            if (entries.Count >= MaxEntries || entries.Values.Count(e => e.RemoteIp == remoteIp) >= MaxEntriesPerIp)
            {
                return new EnrollResult(EnrollOutcome.TooMany);
            }
            var created = new Entry(instanceId, edgeName, machineName, platform, remoteIp, now, now, secretHash);
            entries[instanceId] = created;
            return ToResult(created);
        }
    }

    public EnrollResult GetStatus(string instanceId, byte[] secretHash)
    {
        lock (gate)
        {
            Purge();
            // Same answer for "no such entry" and "wrong secret" - don't confirm that an InstanceId exists.
            if (!entries.TryGetValue(instanceId, out var entry) || !CryptographicOperations.FixedTimeEquals(entry.SecretHash, secretHash))
            {
                return new EnrollResult(EnrollOutcome.Unknown);
            }
            entries[instanceId] = entry with { LastSeenUtc = DateTime.UtcNow };
            return ToResult(entry);
        }
    }

    public void MarkApproved(string instanceId, string accessKey, string edgeName)
    {
        lock (gate)
        {
            if (entries.TryGetValue(instanceId, out var entry))
            {
                entries[instanceId] = entry with { ApprovedAccessKey = accessKey, ApprovedAtUtc = DateTime.UtcNow, ApprovedEdgeName = edgeName };
            }
        }
    }

    public void Remove(string instanceId)
    {
        lock (gate)
        {
            entries.Remove(instanceId);
        }
    }

    // Callers hold the gate. Bounded by MaxEntries, so a full scan per call is cheap.
    private void Purge()
    {
        var now = DateTime.UtcNow;
        var expired = entries.Values
            .Where(e => e.ApprovedAtUtc is { } approvedAt
                ? now - approvedAt > ApprovedEntryLifetime
                : now - e.LastSeenUtc > PendingIdleLifetime)
            .Select(e => e.InstanceId)
            .ToList();
        foreach (var id in expired)
        {
            entries.Remove(id);
        }
    }

    private static EnrollResult ToResult(Entry entry) =>
        entry.ApprovedAccessKey != null
            ? new EnrollResult(EnrollOutcome.Approved, entry.ApprovedAccessKey, entry.ApprovedEdgeName)
            : new EnrollResult(EnrollOutcome.Pending);

    private static PendingEdgeInfo ToInfo(Entry entry) =>
        new(entry.InstanceId, entry.EdgeName, entry.MachineName, entry.Platform, entry.RemoteIp, entry.FirstSeenUtc, entry.LastSeenUtc);
}
