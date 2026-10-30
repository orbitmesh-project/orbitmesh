using System.Security.Cryptography;
using System.Text;

namespace OrbitMesh.Server.Services;

/// <summary>
/// Salted PBKDF2-SHA256 over the password the user actually typed, for Human credentials. Only ever run at
/// sign-in (a session cookie carries every later request, see <see cref="SessionStore"/>), which is what
/// allows the OWASP-recommended iteration count here, unlike <see cref="AccessKeyHasher"/> that had to stay
/// cheap because it ran on every request. The iteration count is part of the stored value, so it can be
/// raised later and existing hashes are upgraded at their next successful sign-in.
/// </summary>
public static class PasswordHasher
{
    private const string Prefix = "PWD$";
    private const int Iterations = 600_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    // Verified against when the user name doesn't exist, so "unknown user" costs the same time as
    // "wrong password" and the response time doesn't tell the two apart.
    private static readonly Lazy<string> DummyHash = new(() => Hash("orbitmesh-timing-equalizer"));

    public static bool IsHashed(string value) => value.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Prefix}{Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify(string hashed, string password)
    {
        if (!TryParse(hashed, out var iterations, out var salt, out var expected))
        {
            return false;
        }
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    /// <summary>True for a hash made with fewer iterations than the current count.</summary>
    public static bool NeedsRehash(string hashed) =>
        TryParse(hashed, out var iterations, out _, out _) && iterations < Iterations;

    public static void BurnVerify(string password) => Verify(DummyHash.Value, password);

    /// <summary>What the Console used to send as a Human credential's key before sessions existed:
    /// SHA-1 of "username" + "password", lower-case hex. Servers up to 1.2.x stored a PBKDF2 hash of it
    /// (<see cref="AccessKeyHasher"/>); signing in still accepts that form so existing accounts keep working,
    /// and upgrades it to a real password hash on the way.</summary>
    public static string LegacyAccessKey(string username, string password) =>
        Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(username + password)));

    private static bool TryParse(string hashed, out int iterations, out byte[] salt, out byte[] key)
    {
        iterations = 0;
        salt = key = [];
        if (!IsHashed(hashed))
        {
            return false;
        }
        var parts = hashed[Prefix.Length..].Split('$');
        if (parts.Length != 3 || !int.TryParse(parts[0], out iterations) || iterations <= 0)
        {
            return false;
        }
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            key = Convert.FromBase64String(parts[2]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
