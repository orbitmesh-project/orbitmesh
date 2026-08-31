using System.Collections.Concurrent;

namespace OrbitMesh.Server.Services;

/// <summary>
/// Tracks in-flight saga (request/response) exchanges so <see cref="IOrbitMeshDirectory.CheckMessageAuthorization"/>
/// can only bypass <c>Authorizations.Messages</c> for a "__Response" message when it's genuinely
/// replying to a saga this server actually saw go out - not a SagaId/target the client made up
/// itself (see CodeQL cs/user-controlled-bypass: <c>scope.IsSaga</c> is entirely client-controlled,
/// so the old unconditional bypass let any credential holding just <c>messages:execute</c> reach any
/// target by claiming to be answering a saga that never happened).
///
/// Purely in-memory, like <see cref="LoginAttemptLimiter"/> - a restart just drops in-flight sagas,
/// which resolve within seconds under normal use anyway. One-shot: a matched response consumes its
/// entry so it can't be replayed.
/// </summary>
public sealed class SagaRegistry
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private const int SweepEveryNRegistrations = 200;

    private sealed record PendingSaga(string OriginatorTarget, DateTime ExpiresAtUtc);

    private readonly ConcurrentDictionary<string, PendingSaga> pending = new();
    private int registrationsSinceSweep;

    /// <summary>Records that <paramref name="sender"/> just sent out a saga-tagged request - the only
    /// party <see cref="TryConsumeResponse"/> will later accept a "__Response" on this SagaId for.
    /// Mirrors <c>MessageExtension.CreateResponseScope</c>'s own sender-to-target mapping.</summary>
    public void RegisterRequest(string sagaId, MessageSender sender)
    {
        var target = sender.Type == MessageSender.SenderType.ConsumerHub ? sender.ConnectionId : sender.FriendlyName;
        if (string.IsNullOrEmpty(target))
        {
            return;
        }
        pending[sagaId] = new PendingSaga(target, DateTime.UtcNow + Lifetime);
        if (Interlocked.Increment(ref registrationsSinceSweep) >= SweepEveryNRegistrations)
        {
            Interlocked.Exchange(ref registrationsSinceSweep, 0);
            SweepExpired();
        }
    }

    /// <summary>True if <paramref name="scope"/> is a legitimate, still-pending response to
    /// <paramref name="sagaId"/> - i.e. addressed back (as a single-target <c>Package</c> scope)
    /// to whoever actually sent that saga's request. Consumes the entry either way, so a given
    /// saga can only be answered - or guessed at - once.</summary>
    public bool TryConsumeResponse(string sagaId, MessageScope scope)
    {
        if (!pending.TryRemove(sagaId, out var saga) || DateTime.UtcNow > saga.ExpiresAtUtc)
        {
            return false;
        }
        return scope.Scope == MessageScope.ScopeType.Package
            && scope.Args.Count == 1
            && scope.Args[0] == saga.OriginatorTarget;
    }

    private void SweepExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var (id, saga) in pending)
        {
            if (now > saga.ExpiresAtUtc)
            {
                pending.TryRemove(id, out _);
            }
        }
    }
}
