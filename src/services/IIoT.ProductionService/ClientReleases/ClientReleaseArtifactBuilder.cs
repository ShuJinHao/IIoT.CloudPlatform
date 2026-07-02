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
                $"installers/{EscapePath(channel)}/{EscapePath(version)}")
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
                $"plugins/{EscapePath(channel)}/{EscapePath(moduleId)}/{EscapePath(version)}")
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

    public static ClientReleaseArtifact VelopackFile(
        string channel,
        string relativePath)
    {
        return new ClientReleaseArtifact(
            ClientReleaseArtifactKind.VelopackFile,
            $"velopack/{EscapePath(channel)}/{relativePath.Replace('\\', '/').TrimStart('/')}");
    }

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

    private static string EscapePath(string value)
    {
        return Uri.EscapeDataString(value.Trim());
    }
}
