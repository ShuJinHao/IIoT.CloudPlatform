using IIoT.Core.Production.Aggregates.ClientReleases;

namespace IIoT.ProductionService.ClientReleases;

internal static class ClientReleaseArtifactBuilder
{
    private const string EdgeUpdatesPrefix = "/edge-updates/";

    public static IReadOnlyList<ClientReleaseArtifact> FromHostDownloadUrl(
        string downloadUrl,
        string channel,
        string version,
        string? sha256 = null,
        long? size = null)
    {
        var artifacts = new List<ClientReleaseArtifact>
        {
            new(
                ClientReleaseArtifactKind.InstallerDirectory,
                $"installers/{EscapePathSegment(channel)}/{EscapePathSegment(version)}")
        };

        var relativePath = TryExtractEdgeUpdatesPath(downloadUrl);
        if (relativePath is not null)
        {
            artifacts.Add(new ClientReleaseArtifact(
                ClientReleaseArtifactKind.ManifestFile,
                relativePath,
                sha256,
                size));
        }

        return artifacts;
    }

    public static IReadOnlyList<ClientReleaseArtifact> FromPluginDownloadUrl(
        string downloadUrl,
        string channel,
        string moduleId,
        string version,
        string? sha256 = null,
        long? size = null)
    {
        var artifacts = new List<ClientReleaseArtifact>
        {
            new(
                ClientReleaseArtifactKind.PluginPackageDirectory,
                $"plugins/{EscapePathSegment(channel)}/{EscapePathSegment(moduleId)}/{EscapePathSegment(version)}")
        };

        var relativePath = TryExtractEdgeUpdatesPath(downloadUrl);
        if (relativePath is not null)
        {
            artifacts.Add(new ClientReleaseArtifact(
                ClientReleaseArtifactKind.PackageFile,
                relativePath,
                sha256,
                size));
        }

        return artifacts;
    }

    public static IReadOnlyList<ClientReleaseArtifact> FromPublishedHostFiles(
        string manifestDownloadUrl,
        string channel,
        string version,
        ClientReleaseFileFact manifest,
        string installerStubRelativePath,
        ClientReleaseFileFact installerStub)
    {
        var artifacts = new List<ClientReleaseArtifact>
        {
            new(
                ClientReleaseArtifactKind.InstallerDirectory,
                $"installers/{EscapePathSegment(channel)}/{EscapePathSegment(version)}")
        };

        var manifestRelativePath = TryExtractEdgeUpdatesPath(manifestDownloadUrl)
            ?? throw new ClientReleaseValidationException("Edge installer manifest 下载地址非法。");
        artifacts.Add(new ClientReleaseArtifact(
            ClientReleaseArtifactKind.ManifestFile,
            manifestRelativePath,
            manifest.Sha256,
            manifest.Size));
        artifacts.Add(new ClientReleaseArtifact(
            ClientReleaseArtifactKind.PackageFile,
            $"installers/{EscapePathSegment(channel)}/{EscapePathSegment(version)}/{installerStubRelativePath.Replace('\\', '/').TrimStart('/')}",
            installerStub.Sha256,
            installerStub.Size));
        return artifacts;
    }

    public static ClientReleaseArtifact VelopackFile(
        string channel,
        string relativePath,
        string sha256,
        long size)
    {
        return new ClientReleaseArtifact(
            ClientReleaseArtifactKind.VelopackFile,
            $"velopack/{EscapePathSegment(channel)}/{relativePath.Replace('\\', '/').TrimStart('/')}",
            sha256,
            size);
    }

    public static string BuildInstallerManifestDownloadUrl(string channel, string version)
        => $"/edge-updates/installers/{EscapeUrlSegment(channel)}/{EscapeUrlSegment(version)}/installer-artifact.json";

    public static string BuildPluginDownloadUrl(
        string channel,
        string moduleId,
        string version,
        string packageFileName)
        => $"/edge-updates/plugins/{EscapeUrlSegment(channel)}/{EscapeUrlSegment(moduleId)}/{EscapeUrlSegment(version)}/{EscapeUrlSegment(packageFileName)}";

    public static string EscapePathSegment(string value)
        => Uri.EscapeDataString(value.Trim());

    private static string EscapeUrlSegment(string value)
        => Uri.EscapeDataString(value);

    public static string? TryExtractEdgeUpdatesPath(string? downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return null;
        }

        var normalized = downloadUrl.Trim();
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absolute))
        {
            normalized = absolute.AbsolutePath;
        }

        if (!normalized.StartsWith(EdgeUpdatesPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Uri.UnescapeDataString(normalized[EdgeUpdatesPrefix.Length..])
            .Replace('\\', '/')
            .TrimStart('/');
    }
}
