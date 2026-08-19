namespace OrbitMesh.Updating;

/// <summary>Shared "is this actually a newer version" check - used by the Server's own update status,
/// the static site (Console, or any other) update status, and the Edge's.</summary>
public static class UpdateVersionComparer
{
    /// <summary>True if <paramref name="latest"/> is actually newer than <paramref name="current"/> -
    /// also true if <paramref name="current"/> is unset (nothing to compare against yet), false if
    /// <paramref name="latest"/> is unset.</summary>
    public static bool IsNewer(string? latest, string? current)
    {
        if (string.IsNullOrEmpty(latest))
        {
            return false;
        }
        if (string.IsNullOrEmpty(current))
        {
            // Never checked in successfully before - anything reported counts as available.
            return true;
        }
        // Strip SemVer build metadata ("+...", e.g. a git sha the SDK can append) before comparing -
        // it's not significant for version precedence, but would break an exact-match fallback.
        var currentCore = current.Split('+', 2)[0];
        var latestCore = latest.Split('+', 2)[0];
        return Version.TryParse(currentCore, out var c) && Version.TryParse(latestCore, out var l)
            ? l > c
            : !string.Equals(latestCore, currentCore, StringComparison.OrdinalIgnoreCase);
    }
}
