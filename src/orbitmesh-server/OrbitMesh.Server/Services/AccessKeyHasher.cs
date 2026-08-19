using System.Security.Cryptography;

namespace OrbitMesh.Server.Services;

/// <summary>
/// Salted PBKDF2-SHA256 for Human credentials only (see <see cref="Configuration.CredentialKind"/>) -
/// Machine credentials stay plaintext at rest since the Server hands them back out verbatim.
/// Iteration count is deliberately modest (not OWASP's ~600k password-hash recommendation): this
/// runs on every Management API/Console request from a signed-in human, not just at login (there is
/// no separate session-token concept here - the same value re-authenticates every request), and
/// human/console traffic volume is low enough that this is still a meaningful brute-force cost
/// without adding noticeable request latency.
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
