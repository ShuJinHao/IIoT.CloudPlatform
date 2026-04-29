using System.Security.Cryptography;

namespace IIoT.ProductionService.Security;

public static class BootstrapSecretGenerator
{
    private const int SecretByteLength = 32;

    public static string Generate()
    {
        return Base64UrlEncode(RandomNumberGenerator.GetBytes(SecretByteLength));
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
