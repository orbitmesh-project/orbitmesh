namespace OrbitMesh;

/// <summary>How a package (or the Server/Edge process itself) is restarted after a crash - restart up
/// to <see cref="NumberOfRetry"/> times within <see cref="ResetCounterAfterMinutes"/>, then stay down
/// for manual investigation.</summary>
public sealed class RecoveryOptions : IEquatable<RecoveryOptions>
{
    /// <summary>Whether to restart automatically at all.</summary>
    public bool RestartAfterFailure { get; set; }

    /// <summary>How many times to restart within the reset window before giving up.</summary>
    public int NumberOfRetry { get; set; }

    /// <summary>The failure-count window, in minutes - a crash older than this doesn't count toward
    /// <see cref="NumberOfRetry"/>.</summary>
    public int ResetCounterAfterMinutes { get; set; }

    /// <summary>Delay before each restart attempt, in seconds.</summary>
    public int RestartPackageAfterSeconds { get; set; }

    /// <inheritdoc/>
    public bool Equals(RecoveryOptions? other)
    {
        if (other is null) return false;
        return NumberOfRetry == other.NumberOfRetry
            && ResetCounterAfterMinutes == other.ResetCounterAfterMinutes
            && RestartAfterFailure == other.RestartAfterFailure
            && RestartPackageAfterSeconds == other.RestartPackageAfterSeconds;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as RecoveryOptions);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(NumberOfRetry, ResetCounterAfterMinutes, RestartAfterFailure, RestartPackageAfterSeconds);

    /// <summary>Value equality - see <see cref="Equals(RecoveryOptions?)"/>.</summary>
    public static bool operator ==(RecoveryOptions? a, RecoveryOptions? b) => a is null ? b is null : a.Equals(b);

    /// <summary>Value inequality - see <see cref="Equals(RecoveryOptions?)"/>.</summary>
    public static bool operator !=(RecoveryOptions? a, RecoveryOptions? b) => !(a == b);
}
