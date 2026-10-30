using System.Security.Cryptography;

namespace OrbitMesh.Server.Services;

/// <summary>
/// LEGACY hash of a Human credential, from before sessions existed: PBKDF2-SHA256 (deliberately modest, 20,000
/// iterations, because it ran on every request) over the SHA-1 of "username + password" that the old Console
/// computed itself and sent as its AccessKey. New hashes are made by <see cref="PasswordHasher"/>; this class
/// only remains so an account created before can still sign in once (AuthController then upgrades the hash)
/// and so an old Console keeps working until then.
/// </summary>
public static class AccessKeyHasher
{
    private const string Prefix = "PBKDF2$";
    private const int Iterations = 20_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public static bool IsHashed(string value) => value.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Hash(string plainAccessKey)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(plainAccessKey, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Prefix}{Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify(string hashed, string presentedAccessKey)
    {
        if (!IsHashed(hashed))
        {
            return false;
        }
        var parts = hashed[Prefix.Length..].Split('$');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
        {
            return false;
        }
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expected = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }
        var actual = Rfc2898DeriveBytes.Pbkdf2(presentedAccessKey, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
