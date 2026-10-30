using System.Security.Cryptography;
using System.Text;

namespace OrbitMesh.Server.Services;

/// <summary>
/// The API token format: <c>omk_&lt;id&gt;_&lt;secret&gt;</c>, with a 16-hex-character id (public) and a 43-character
/// base64url secret (32 random bytes). The fixed prefix lets a secret scanner (GitHub's, gitleaks...) recognise
/// a leaked token; the id lets the Server find the one credential it belongs to without testing them all; the
/// Server keeps only <c>TKN$</c> + the SHA-256 of the whole token. A random 256-bit secret has nothing to gain
/// from a slow password hash, and nothing ever needs to read it back (unlike a Machine key, which the Server
/// hands to the Edge that launches a package).
/// </summary>
public static class ApiTokens
{
    public const string Prefix = "omk_";

    private const string StoredPrefix = "TKN$";
    private const int IdLength = 16;
    private const int SecretLength = 43;
    private const int TokenLength = 4 + IdLength + 1 + SecretLength;

    public sealed record NewToken(string Id, string Token, string Stored);

    public static NewToken Generate()
    {
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(IdLength / 2));
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var token = $"{Prefix}{id}_{secret}";
        return new NewToken(id, token, Hash(token));
    }

    /// <summary>True for a value shaped like a token; <paramref name="id"/> is then its public id. Says nothing
    /// about whether it is a real one.</summary>
    public static bool TryParse(string? presented, out string id)
    {
        id = string.Empty;
        if (presented is not { Length: TokenLength } || !presented.StartsWith(Prefix, StringComparison.Ordinal) || presented[Prefix.Length + IdLength] != '_')
        {
            return false;
        }
        var candidate = presented.Substring(Prefix.Length, IdLength);
        if (!candidate.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
        {
            return false;
        }
        id = candidate;
        return true;
    }

    public static bool IsHashed(string value) => value.StartsWith(StoredPrefix, StringComparison.Ordinal);

    public static string Hash(string token) => StoredPrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static bool Verify(string stored, string presented)
    {
        if (!IsHashed(stored))
        {
            return false;
        }
        // Compare the two digests, not the strings: constant time, and equal length by construction.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(stored),
            Encoding.ASCII.GetBytes(Hash(presented)));
    }
}
