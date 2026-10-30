using System.Security.Cryptography;

namespace OrbitMesh.Server.Tests.Harness;

/// <summary>The server project folder holds the developer's own appsettings.json and keys/ (secrets, git-ignored).
/// A test server that picked that folder as its content root would read - and, on a write, change - them. The
/// harness points every server at a temporary folder instead; this guard makes a mistake loud: it fingerprints
/// those files before the first server starts and fails if they differ afterwards.</summary>
public static class DevFilesGuard
{
    private static readonly Lazy<Dictionary<string, string>> Baseline = new(Snapshot);

    public static void Capture() => _ = Baseline.Value;

    public static void AssertUntouched()
    {
        var now = Snapshot();
        var before = Baseline.Value;
        var changed = before.Keys.Union(now.Keys)
            .Where(k => !before.TryGetValue(k, out var b) || !now.TryGetValue(k, out var n) || b != n)
            .ToList();
        if (changed.Count > 0)
        {
            throw new InvalidOperationException(
                "A test run changed the developer's own server files: " + string.Join(", ", changed) +
                ". The test server was not isolated in its temporary folder.");
        }
    }

    private static Dictionary<string, string> Snapshot()
    {
        var result = new Dictionary<string, string>();
        var projectDir = FindServerProjectDirectory();
        if (projectDir == null)
        {
            return result;
        }
        var files = new List<string> { Path.Combine(projectDir, "appsettings.json") };
        var keys = Path.Combine(projectDir, "keys");
        if (Directory.Exists(keys))
        {
            files.AddRange(Directory.GetFiles(keys, "*", SearchOption.AllDirectories));
        }
        foreach (var file in files.Where(File.Exists))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            result[file] = Convert.ToHexString(SHA256.HashData(stream));
        }
        return result;
    }

    internal static string? FindServerProjectDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "OrbitMesh.Server", "OrbitMesh.Server.csproj");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate);
            }
        }
        return null;
    }
}
