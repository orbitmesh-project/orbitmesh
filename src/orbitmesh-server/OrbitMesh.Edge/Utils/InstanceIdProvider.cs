namespace OrbitMesh.Edge.Utils;

/// <summary>Generates and persists this Edge's InstanceId (see OrbitMeshHeaderNames.InstanceId).</summary>
public static class InstanceIdProvider
{
    private const string FileName = "instance-id.txt";

    /// <summary>Reads the InstanceId from <paramref name="directory"/>, generating and persisting a
    /// new one on first run.</summary>
    public static string GetOrCreate(string directory)
    {
        var path = Path.Combine(directory, FileName);
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (Guid.TryParse(existing, out _))
            {
                return existing;
            }
            // Corrupted/empty file - regenerate.
        }
        var id = Guid.NewGuid().ToString();
        File.WriteAllText(path, id);
        return id;
    }
}
