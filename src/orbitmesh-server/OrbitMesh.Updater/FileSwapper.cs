namespace OrbitMesh.Updater;

/// <summary>Swaps a staged release into place with two directory renames, same pattern used by
/// ServerSelfUpdater/StaticSiteUpdater before this project existed - the live directory is renamed aside
/// instead of overwritten in place, so nothing ever observes a half-written tree, and the backup this
/// produces is exactly what Rollback needs if the new version turns out not to start.</summary>
internal static class FileSwapper
{
    public static string Swap(string liveDirectory, string stagingDirectory)
    {
        var backupDirectory = GetBackupDirectory(liveDirectory);
        if (Directory.Exists(backupDirectory))
        {
            Directory.Delete(backupDirectory, recursive: true);
        }

        var liveDirectoryExisted = Directory.Exists(liveDirectory);
        if (liveDirectoryExisted)
        {
            Directory.Move(liveDirectory, backupDirectory);
        }
        try
        {
            Directory.Move(stagingDirectory, liveDirectory);
        }
        catch
        {
            // Put the previous version straight back rather than leave the install directory missing
            // entirely if this second rename fails after the first one already succeeded.
            if (liveDirectoryExisted && Directory.Exists(backupDirectory) && !Directory.Exists(liveDirectory))
            {
                Directory.Move(backupDirectory, liveDirectory);
            }
            throw;
        }
        return backupDirectory;
    }

    public static void Rollback(string liveDirectory, string backupDirectory)
    {
        if (Directory.Exists(liveDirectory))
        {
            Directory.Delete(liveDirectory, recursive: true);
        }
        Directory.Move(backupDirectory, liveDirectory);
    }

    private static string GetBackupDirectory(string liveDirectory)
    {
        var fullLiveDirectory = Path.GetFullPath(liveDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var parent = Path.GetDirectoryName(fullLiveDirectory)
            ?? throw new InvalidOperationException($"Cannot determine the parent of '{fullLiveDirectory}'.");
        return Path.Combine(parent, $"__update_backup_{Path.GetFileName(fullLiveDirectory)}__");
    }
}
