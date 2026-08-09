namespace IIoT.ProductionService.ClientReleases;

using System.Security.Cryptography;

public sealed class EdgeInstallerArtifactOptions
{
    public const string SectionName = "EdgeInstallerArtifacts";

    public string RootPath { get; set; } = "edge-updates/installers";

    public string? VelopackReleasesBaseUrl { get; set; }

    /// <summary>受 Edge 预置信任的发布签名 key id；不属于业务身份。</summary>
    public string? PayloadSigningKeyId { get; set; }

    /// <summary>RSA PKCS#8/PKCS#1 PEM 私钥文件。生产应由 secret mount 提供。</summary>
    public string? PayloadSigningPrivateKeyPemPath { get; set; }

    /// <summary>仅用于 secret provider/测试注入，不写入数据库或安装包。</summary>
    public string? PayloadSigningPrivateKeyPem { get; set; }

    public void Validate(bool requireMountedKeyMaterial = false)
    {
        if (string.IsNullOrWhiteSpace(RootPath))
        {
            throw new InvalidOperationException($"{SectionName}:RootPath must be configured.");
        }

        if (string.IsNullOrWhiteSpace(PayloadSigningKeyId))
        {
            throw new InvalidOperationException(
                $"{SectionName}:PayloadSigningKeyId must be configured.");
        }
        if (PayloadSigningKeyId.Trim().Length > 128)
        {
            throw new InvalidOperationException(
                $"{SectionName}:PayloadSigningKeyId is too long.");
        }
        if (string.IsNullOrWhiteSpace(PayloadSigningPrivateKeyPemPath)
            && string.IsNullOrWhiteSpace(PayloadSigningPrivateKeyPem))
        {
            throw new InvalidOperationException(
                $"{SectionName}: a payload signing private key source must be configured.");
        }


        if (requireMountedKeyMaterial)
        {
            if (!string.IsNullOrWhiteSpace(PayloadSigningPrivateKeyPem))
            {
                throw new InvalidOperationException(
                    $"{SectionName}: production must use a read-only mounted private-key file.");
            }

            var keyPath = Path.GetFullPath(PayloadSigningPrivateKeyPemPath!);
            if (!File.Exists(keyPath))
            {
                throw new InvalidOperationException(
                    $"{SectionName}: mounted payload signing private key is missing.");
            }

            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(File.ReadAllText(keyPath));
                if (rsa.KeySize < 2048)
                {
                    throw new InvalidOperationException(
                        $"{SectionName}: payload signing RSA key must be at least 2048 bits.");
                }
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or CryptographicException
                    or ArgumentException)
            {
                throw new InvalidOperationException(
                    $"{SectionName}: mounted payload signing private key is invalid.",
                    exception);
            }
        }
    }

    public string? BuildVelopackUpdateSource(string channel)
    {
        if (string.IsNullOrWhiteSpace(VelopackReleasesBaseUrl))
        {
            return null;
        }

        return $"{VelopackReleasesBaseUrl.TrimEnd('/')}/{channel}/";
    }

    public string ResolveEdgeUpdatesRoot()
    {
        var installerRoot = Path.GetFullPath(RootPath);
        var parent = Directory.GetParent(installerRoot);
        if (parent is null)
        {
            throw new InvalidOperationException(
                $"{SectionName}:RootPath 必须位于 edge-updates/installers 下。");
        }

        return parent.FullName;
    }
}
