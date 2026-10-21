using System.Security.Cryptography;

namespace OrbitMesh.Edge.Utils;

/// <summary>Generates and persists the secret this Edge sends on every <c>rest/enroll</c> call (see
/// OrbitMeshHeaderNames.EnrollSecret). Persisted so a restart mid-approval keeps polling the same entry.</summary>
public static class EnrollSecretProvider
{
    private const string FileName = "enroll-secret.txt";

    public static string GetOrCreate(string directory)
    {
        var path = Path.Combine(directory, FileName);
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (existing.Length >= 32)
            {
                return existing;
            }
        }
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(path, secret);
        return secret;
    }
}
