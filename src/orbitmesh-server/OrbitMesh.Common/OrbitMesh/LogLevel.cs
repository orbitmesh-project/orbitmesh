namespace OrbitMesh;

/// <summary>Severity for <c>PackageHost.WriteLog</c> and friends - shown in the Console's Console log
/// page, and drives the unread-error badge there for <see cref="Error"/>/<see cref="Fatal"/>.</summary>
public enum LogLevel
{
    /// <summary>Console/local only - never sent to the Server.</summary>
    Debug,
    /// <summary>Routine informational message.</summary>
    Info,
    /// <summary>Something unexpected but non-fatal.</summary>
    Warn,
    /// <summary>An operation failed.</summary>
    Error,
    /// <summary>An unrecoverable error - typically followed by the package shutting down.</summary>
    Fatal
}
