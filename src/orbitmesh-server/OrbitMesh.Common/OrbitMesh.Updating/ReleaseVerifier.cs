using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace OrbitMesh.Updating;

/// <summary>Verifies a downloaded-and-extracted release against its own bundled manifest.json/.sig -
/// a .NET port of Forgelabme\Ci4Updater\Libraries\ReleaseSignature::verify(). RS256 (RSA+SHA-256) over
/// the manifest's exact bytes; the manifest itself carries a SHA-256 hash of every file in the
/// release, so a valid signature covers the whole payload transitively.
///
/// Refuses any release it can't verify: with no public keys configured (UpdateOptions.PublicKeys) the
/// only way to apply one is the explicit UpdateOptions.AllowUnsignedReleases opt-out. Otherwise an
/// unsigned release, one signed by an unknown key, one whose files don't match what the manifest
/// declares, or one whose signed version isn't the version being applied (a replayed older release) is
/// refused outright - release-server.ps1 / release-static-site.ps1 sign with the matching private key.</summary>
public sealed class ReleaseVerifier(ILogger<ReleaseVerifier> logger)
{
    private const string Algorithm = "RS256";
    private const string ManifestFileName = "manifest.json";
    private const string SignatureFileName = "manifest.json.sig";

    /// <summary>Verifies the release staged at <paramref name="stagedDir"/> against its own bundled
    /// manifest.json/.sig. <paramref name="expectedVersion"/> is the version the update server
    /// announced and the caller is about to apply; <paramref name="currentVersion"/> is what's running
    /// now. Throws if the release is unsigned (and unsigned releases aren't explicitly allowed), signed
    /// by an untrusted key, its files don't match the signed manifest, or the signed version isn't
    /// <paramref name="expectedVersion"/> and newer than <paramref name="currentVersion"/>.</summary>
    public void Verify(string stagedDir, UpdateOptions updateOptions, string? expectedVersion, string? currentVersion)
    {
        var trustedPublicKeys = updateOptions.PublicKeys;
        if (trustedPublicKeys.Count == 0)
        {
            if (!updateOptions.AllowUnsignedReleases)
            {
                throw new InvalidOperationException(
                    "Refusing to apply an unverified release: UpdateOptions.PublicKeys is empty, so nothing can vouch for it. " +
                    "Add the release signing public key to UpdateOptions.PublicKeys (recommended), or set UpdateOptions.AllowUnsignedReleases to true " +
                    "to trust the update server connection alone.");
            }
            logger.LogWarning("Applying a release without verifying it (UpdateOptions.AllowUnsignedReleases is set and no PublicKeys are configured).");
            return;
        }

        var manifestPath = Path.Combine(stagedDir, ManifestFileName);
        var signaturePath = Path.Combine(stagedDir, SignatureFileName);
        if (!File.Exists(manifestPath) || !File.Exists(signaturePath))
        {
            throw new InvalidOperationException(
                "This install requires signed releases (UpdateOptions.PublicKeys is set), but the downloaded release has no manifest.json/manifest.json.sig.");
        }

        var manifestBytes = File.ReadAllBytes(manifestPath);
        var envelope = File.ReadAllText(signaturePath);
        if (!VerifySignature(manifestBytes, envelope, trustedPublicKeys))
        {
            throw new InvalidOperationException("Release signature verification failed - refusing to apply an unsigned or tampered release.");
        }

        var manifest = VerifyFileHashes(stagedDir, manifestBytes);
        VerifyVersion(manifest.Version, expectedVersion, currentVersion);
        logger.LogInformation("Release signature, version and file hashes verified against manifest.json.");
    }

    // The signature covers the manifest's own "version", unlike latest.json's (unsigned) one - without
    // this, an older but validly-signed release could be replayed as if it were the announced update.
    private static void VerifyVersion(string? signedVersion, string? expectedVersion, string? currentVersion)
    {
        if (string.IsNullOrEmpty(signedVersion))
        {
            throw new InvalidOperationException("The signed manifest carries no version - refusing to apply a release that can't be tied to the announced version.");
        }
        if (!string.Equals(Core(signedVersion), Core(expectedVersion), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The signed manifest is for version '{signedVersion}', not the announced '{expectedVersion}' - refusing to apply it.");
        }
        if (!string.IsNullOrEmpty(currentVersion) && !UpdateVersionComparer.IsNewer(signedVersion, currentVersion))
        {
            throw new InvalidOperationException($"The signed release version '{signedVersion}' is not newer than the running '{currentVersion}' - refusing a downgrade.");
        }
    }

    private static string Core(string? version) => (version ?? string.Empty).Split('+', 2)[0].Trim();

    private static bool VerifySignature(byte[] payload, string envelopeJson, IReadOnlyList<string> publicKeys)
    {
        JsonElement envelope;
        try
        {
            envelope = JsonDocument.Parse(envelopeJson).RootElement;
        }
        catch (JsonException)
        {
            return false;
        }
        if (!envelope.TryGetProperty("alg", out var algElement) || algElement.GetString() != Algorithm
            || !envelope.TryGetProperty("signature", out var sigElement))
        {
            return false;
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(sigElement.GetString() ?? string.Empty);
        }
        catch (FormatException)
        {
            return false;
        }
        if (signature.Length == 0)
        {
            return false;
        }

        foreach (var keyPemOrPath in publicKeys)
        {
            var pem = ReadKey(keyPemOrPath);
            if (pem == null)
            {
                continue;
            }
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(pem);
                if (rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                {
                    return true;
                }
            }
            catch (CryptographicException)
            {
                // Malformed key, or simply not the one that produced this signature - try the next one,
                // same "several keys may be listed to rotate without downtime" policy as the PHP side.
            }
        }
        return false;
    }

    // Rejects a mismatch in EITHER direction: a file the manifest declares but the release doesn't
    // contain, or one the release contains but the (signed) manifest never mentions - the latter is
    // exactly how a tampered zip could smuggle in an extra file the signature never covered.
    private static ManifestDocument VerifyFileHashes(string stagedDir, byte[] manifestBytes)
    {
        var manifest = JsonSerializer.Deserialize<ManifestDocument>(manifestBytes)
            ?? throw new InvalidOperationException("manifest.json could not be parsed.");
        var declared = manifest.Files ?? new Dictionary<string, string>();

        var onDisk = Directory.GetFiles(stagedDir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(stagedDir, f).Replace('\\', '/'))
            .Where(f => f != ManifestFileName && f != SignatureFileName)
            .ToHashSet(StringComparer.Ordinal);

        var missing = declared.Keys.Where(f => !onDisk.Contains(f)).ToList();
        var extra = onDisk.Where(f => !declared.ContainsKey(f)).ToList();
        if (missing.Count > 0 || extra.Count > 0)
        {
            throw new InvalidOperationException(
                $"Release contents don't match its manifest (missing: [{string.Join(", ", missing)}], unexpected: [{string.Join(", ", extra)}]).");
        }

        foreach (var (relativePath, expectedHash) in declared)
        {
            var actualHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(stagedDir, relativePath))));
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"File hash mismatch for '{relativePath}' - the release may be corrupted or tampered with.");
            }
        }
        return manifest;
    }

    // Same convenience as ReleaseSignature::readKey() on the PHP side: PEM contents inline, or a path
    // (resolved from the Server's own directory) to a PEM file - lets a deployment keep the key out of
    // appsettings.json entirely if it prefers.
    private static string? ReadKey(string keyOrPath)
    {
        if (keyOrPath.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            return keyOrPath;
        }
        var path = Path.IsPathRooted(keyOrPath) ? keyOrPath : Path.Combine(AppContext.BaseDirectory, keyOrPath);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private sealed class ManifestDocument
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("files")]
        public Dictionary<string, string>? Files { get; set; }
    }
}
