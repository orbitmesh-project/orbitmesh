using System.Security.Cryptography;

namespace OrbitMesh.Server.Services;

/// <summary>
/// Reversible AES-256-GCM encryption for Machine credentials' AccessKey - unlike Human credentials
/// (one-way PBKDF2, see AccessKeyHasher), the Server has to be able to read a Machine key back out to
/// hand it to an Edge/package (see PackageDescription.AccessKey), so a one-way hash isn't an option
/// here. Encrypting it still means a leak of appsettings.json ALONE doesn't hand over every key - the
/// encryption key lives in its own file, outside the config, so both have to be compromised together.
///
/// The Console itself still never re-displays a Machine key once created (see Credentials.js's "shown
/// once" flow) - encryption is what lets the *Server* read it back for automation, not what lets an
/// admin browse back to it later.
/// </summary>
public sealed class AccessKeyCipher
{
    private const string Prefix = "ENC$";
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] key;

    public AccessKeyCipher(IHostEnvironment environment)
    {
        var keyPath = Path.Combine(environment.ContentRootPath, "keys", "credential-encryption.key");
        key = LoadOrCreateKey(keyPath);
    }

    public static bool IsEncrypted(string value) => value.StartsWith(Prefix, StringComparison.Ordinal);

    public string Encrypt(string plainAccessKey)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plainAccessKey);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        return $"{Prefix}{Convert.ToBase64String(nonce)}${Convert.ToBase64String(cipherBytes)}${Convert.ToBase64String(tag)}";
    }

    /// <summary>Throws if <paramref name="encrypted"/> isn't a value this cipher produced - callers on
    /// the hot auth path should check <see cref="IsEncrypted"/> first and treat a legacy plaintext
    /// value as a direct string comparison instead (see OrbitMeshDirectory.MatchesAccessKey).</summary>
    public string Decrypt(string encrypted)
    {
        var parts = encrypted[Prefix.Length..].Split('$');
        if (parts.Length != 3)
        {
            throw new FormatException("Not a value produced by AccessKeyCipher.Encrypt.");
        }
        var nonce = Convert.FromBase64String(parts[0]);
        var cipherBytes = Convert.FromBase64String(parts[1]);
        var tag = Convert.FromBase64String(parts[2]);
        var plainBytes = new byte[cipherBytes.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return System.Text.Encoding.UTF8.GetString(plainBytes);
    }

    // First run on a given install generates its own key - never checked in, never shared across
    // installs (matches the D:\keys\release-signing.* convention already used for release signatures,
    // just per-install instead of per-maintainer).
    private static byte[] LoadOrCreateKey(string path)
    {
        if (File.Exists(path))
        {
            return Convert.FromBase64String(File.ReadAllText(path).Trim());
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var newKey = RandomNumberGenerator.GetBytes(KeySize);
        File.WriteAllText(path, Convert.ToBase64String(newKey));
        return newKey;
    }
}
