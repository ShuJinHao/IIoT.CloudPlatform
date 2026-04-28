using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;

namespace IIoT.Infrastructure.Authentication;

public static class JwtSecretResolver
{
    private const int DevelopmentSecretByteLength = 32;
    private const string DevelopmentSecretDirectoryName = "IIoT.CloudPlatform";

    public static string Resolve(IHostEnvironment environment, string? configuredSecret)
    {
        if (!string.IsNullOrWhiteSpace(configuredSecret))
        {
            return configuredSecret;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "JwtSettings:Secret is missing. Configure it via user-secrets or environment variables.");
        }

        return ResolveDevelopmentSecret(environment.ApplicationName);
    }

    private static string ResolveDevelopmentSecret(string applicationName)
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = Path.GetTempPath();
        }

        var directory = Path.Combine(basePath, DevelopmentSecretDirectoryName);
        Directory.CreateDirectory(directory);

        var fileName = $"{SanitizeFileName(applicationName)}.development-jwt-secret";
        var secretPath = Path.Combine(directory, fileName);

        if (File.Exists(secretPath))
        {
            var existingSecret = File.ReadAllText(secretPath).Trim();
            if (!string.IsNullOrWhiteSpace(existingSecret))
            {
                return existingSecret;
            }
        }

        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(DevelopmentSecretByteLength));
        File.WriteAllText(secretPath, secret);
        return secret;
    }

    private static string SanitizeFileName(string applicationName)
    {
        var candidate = string.IsNullOrWhiteSpace(applicationName)
            ? DevelopmentSecretDirectoryName
            : applicationName.Trim();

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            candidate = candidate.Replace(invalidChar, '_');
        }

        return candidate;
    }
}
