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
        // Falls back to a plain string comparison when either side isn't a parseable Version
        // (e.g. a pre-release suffix) - still correct for the common case, just less precise.
        return Version.TryParse(current, out var c) && Version.TryParse(latest, out var l)
            ? l > c
            : !string.Equals(latest, current, StringComparison.OrdinalIgnoreCase);
    }
}
