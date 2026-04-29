using System.Security.Cryptography;
using System.Text;

namespace IIoT.ProductionService.Security;

public static class BootstrapSecretHasher
{
    private const string Version = "v1";
    private const int SaltByteLength = 16;

    public static string Hash(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var salt = RandomNumberGenerator.GetBytes(SaltByteLength);
        var hash = ComputeHash(secret.Trim(), salt);
        return $"{Version}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string? secret, string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        var segments = storedHash.Split(':', 3);
        if (segments.Length != 3 || !string.Equals(segments[0], Version, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(segments[1]);
            var expectedHash = Convert.FromBase64String(segments[2]);
            var actualHash = ComputeHash(secret.Trim(), salt);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] ComputeHash(string secret, byte[] salt)
    {
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var payload = new byte[salt.Length + secretBytes.Length];
        Buffer.BlockCopy(salt, 0, payload, 0, salt.Length);
        Buffer.BlockCopy(secretBytes, 0, payload, salt.Length, secretBytes.Length);
        return SHA256.HashData(payload);
    }
}
