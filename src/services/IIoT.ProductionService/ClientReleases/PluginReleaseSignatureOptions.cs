namespace IIoT.ProductionService.ClientReleases;

using System.Security.Cryptography;
using System.Text.Json;

public sealed class PluginReleaseSignatureOptions
{
    public const string SectionName = "PluginReleaseSignatures";

    /// <summary>
    /// Read-only JSON key ring mounted by deployment. It contains public keys
    /// only; private release keys never enter Cloud.
    /// </summary>
    public string TrustedPublicKeysFile { get; set; } = string.Empty;

    public void Validate(bool requireMountedKeyMaterial = false)
    {
        if (string.IsNullOrWhiteSpace(TrustedPublicKeysFile))
        {
            throw new InvalidOperationException(
                $"{SectionName}:TrustedPublicKeysFile must be configured.");
        }

        if (!requireMountedKeyMaterial)
            return;

        var keyRingPath = Path.GetFullPath(TrustedPublicKeysFile);
        if (!File.Exists(keyRingPath))
        {
            throw new InvalidOperationException(
                $"{SectionName}: mounted trusted publisher key ring is missing.");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(keyRingPath));
            var root = document.RootElement;
            if (!root.TryGetProperty("schemaVersion", out var schemaVersion)
                || schemaVersion.GetInt32() != 1
                || !root.TryGetProperty("keys", out var keys)
                || keys.ValueKind != JsonValueKind.Array
                || keys.GetArrayLength() == 0)
            {
                throw new InvalidOperationException(
                    $"{SectionName}: trusted publisher key ring schema is invalid.");
            }

            var keyIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in keys.EnumerateArray())
            {
                var keyId = key.GetProperty("keyId").GetString();
                var algorithm = key.GetProperty("algorithm").GetString();
                var pem = key.GetProperty("publicKeyPem").GetString();
                if (string.IsNullOrWhiteSpace(keyId)
                    || !keyIds.Add(keyId)
                    || !string.Equals(algorithm, "rsa-pss-sha256", StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(pem))
                {
                    throw new InvalidOperationException(
                        $"{SectionName}: trusted publisher key entry is invalid.");
                }

                using var rsa = RSA.Create();
                rsa.ImportFromPem(pem);
                if (rsa.KeySize < 2048)
                {
                    throw new InvalidOperationException(
                        $"{SectionName}: trusted publisher RSA key must be at least 2048 bits.");
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or CryptographicException
                or KeyNotFoundException
                or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"{SectionName}: mounted trusted publisher key ring is invalid.",
                exception);
        }
    }
}
