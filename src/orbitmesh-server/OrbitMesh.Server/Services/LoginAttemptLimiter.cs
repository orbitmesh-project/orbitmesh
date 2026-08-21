using System.Collections.Concurrent;
using System.Net;

namespace OrbitMesh.Server.Services;

/// <summary>
/// Brute-force protection for AccessKeyAuthenticationMiddleware, keyed by remote IP rather than by
/// credential name - a failed attempt's presented key doesn't reliably identify which credential is
/// being guessed at, but the source of repeated failures is always known. Purely in-memory: a restart
/// clears it, which is an acceptable trade for not persisting attacker IPs into appsettings.json.
/// </summary>
public sealed class LoginAttemptLimiter
{
    private const int MaxFailuresPerWindow = 10;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private sealed class AttemptState
    {
        public int Count;
        public DateTime WindowStartUtc;
        public DateTime? LockedUntilUtc;
    }

    private readonly ConcurrentDictionary<IPAddress, AttemptState> attempts = new();

    /// <summary>True if this IP is currently locked out and should be rejected without even checking
    /// the presented credential.</summary>
    public bool IsLockedOut(IPAddress? ip)
    {
        if (ip == null || !attempts.TryGetValue(ip, out var state))
        {
            return false;
        }
        lock (state)
        {
            return state.LockedUntilUtc is { } until && DateTime.UtcNow < until;
        }
    }

    /// <summary>Returns true if this failure just tripped the lockout (worth logging loudly).</summary>
    public bool RecordFailure(IPAddress? ip)
    {
        if (ip == null)
        {
            return false;
        }
        var state = attempts.GetOrAdd(ip, _ => new AttemptState { WindowStartUtc = DateTime.UtcNow });
        lock (state)
        {
            var now = DateTime.UtcNow;
            if (state.LockedUntilUtc is { } until && now >= until)
            {
                // Past lockout - start fresh instead of carrying the old count forward.
                state.Count = 0;
                state.WindowStartUtc = now;
                state.LockedUntilUtc = null;
            }
            else if (now - state.WindowStartUtc > Window)
            {
                state.Count = 0;
                state.WindowStartUtc = now;
            }

            state.Count++;
            if (state.Count >= MaxFailuresPerWindow && state.LockedUntilUtc == null)
            {
                state.LockedUntilUtc = now + LockoutDuration;
                return true;
            }
            return false;
        }
    }

    public void RecordSuccess(IPAddress? ip)
    {
        if (ip != null)
        {
            attempts.TryRemove(ip, out _);
        }
    }

    /// <summary>Currently locked-out IPs, for an admin page to list (and offer to clear without a
    /// full restart) - a stale/expired entry that just hasn't been touched since isn't included.</summary>
    public IReadOnlyList<LockedOutIp> GetLockedOutIps()
    {
        var now = DateTime.UtcNow;
        var result = new List<LockedOutIp>();
        foreach (var (ip, state) in attempts)
        {
            lock (state)
            {
                if (state.LockedUntilUtc is { } until && now < until)
                {
                    result.Add(new LockedOutIp(ip.ToString(), state.Count, until));
                }
            }
        }
        return result;
    }

    /// <summary>Clears one IP's lockout (and its failure count) early, without restarting the process.
    /// Returns false if it wasn't locked out to begin with.</summary>
    public bool Unlock(IPAddress ip) => attempts.TryRemove(ip, out _);
}

public sealed record LockedOutIp(string Ip, int FailureCount, DateTime LockedUntilUtc);
