using System.Security.Cryptography.X509Certificates;
using IIoT.Services.Contracts.Identity;

namespace IIoT.HttpApi.Infrastructure.Oidc;

internal static class OidcCertificateLoader
{
    public static X509Certificate2? LoadSigningCertificate(OidcProviderOptions options)
    {
        return LoadCertificate(options.SigningCertificatePath, options.SigningCertificatePassword);
    }

    public static X509Certificate2? LoadEncryptionCertificate(OidcProviderOptions options)
    {
        return LoadCertificate(options.EncryptionCertificatePath, options.EncryptionCertificatePassword);
    }

    private static X509Certificate2? LoadCertificate(string? path, string? password)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return X509CertificateLoader.LoadPkcs12FromFile(path, password);
    }
}
