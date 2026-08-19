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
/// A no-op when no public keys are configured (UpdateOptions.PublicKeys) - same "trust the connection
/// entirely" default ci4-updater itself ships with. As soon as one key is listed, an unsigned release,
/// one signed by an unknown key, or one whose files don't match what the manifest declares is refused
/// outright - release-server.ps1 / release-static-site.ps1 sign with the matching private key.</summary>
public sealed class ReleaseVerifier(ILogger<ReleaseVerifier> logger)
{
    private const string Algorithm = "RS256";
    private const string ManifestFileName = "manifest.json";
    private const string SignatureFileName = "manifest.json.sig";

    /// <summary>Verifies the release staged at <paramref name="stagedDir"/> against its own bundled
    /// manifest.json/.sig, if <paramref name="trustedPublicKeys"/> is non-empty (a no-op otherwise -
    /// see the class remarks). Throws if the release is unsigned, signed by an untrusted key, or its
    /// files don't match the signed manifest.</summary>
    public void VerifyIfRequired(string stagedDir, IReadOnlyList<string> trustedPublicKeys)
    {
        if (trustedPublicKeys.Count == 0)
        {
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

        VerifyFileHashes(stagedDir, manifestBytes);
        logger.LogInformation("Release signature and file hashes verified against manifest.json.");
    }

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
    private static void VerifyFileHashes(string stagedDir, byte[] manifestBytes)
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
        [JsonPropertyName("files")]
        public Dictionary<string, string>? Files { get; set; }
    }
}
