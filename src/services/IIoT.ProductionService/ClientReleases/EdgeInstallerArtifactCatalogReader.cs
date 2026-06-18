using System.Text.Json;
using IIoT.Core.Production.Aggregates.ClientReleases;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.ClientReleases;

public interface IEdgeInstallerArtifactCatalogReader
{
    Task<EdgeInstallerArtifactCatalogSnapshot> ReadAsync(
        string channel,
        string? targetRuntime,
        CancellationToken cancellationToken = default);
}

public sealed record EdgeInstallerArtifactCatalogSnapshot(
    IReadOnlyList<ClientHostRelease> HostReleases,
    IReadOnlyList<ClientPluginRelease> PluginReleases)
{
    public static EdgeInstallerArtifactCatalogSnapshot Empty { get; } = new([], []);
}

public sealed class EdgeInstallerArtifactCatalogReader(
    IOptions<EdgeInstallerArtifactOptions> options)
    : IEdgeInstallerArtifactCatalogReader
{
    private const string ManifestFileName = "installer-artifact.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<EdgeInstallerArtifactCatalogSnapshot> ReadAsync(
        string channel,
        string? targetRuntime,
        CancellationToken cancellationToken = default)
    {
        var normalizedChannel = NormalizeRequired(channel);
        var normalizedTargetRuntime = NormalizeOptional(targetRuntime);
        var rootPath = ResolveRootPath();
        var channelRoot = Path.GetFullPath(Path.Combine(rootPath, normalizedChannel));
        if (!IsChildPath(rootPath, channelRoot) || !Directory.Exists(channelRoot))
        {
            return EdgeInstallerArtifactCatalogSnapshot.Empty;
        }

        var hostReleases = new List<ClientHostRelease>();
        var pluginReleases = new List<ClientPluginRelease>();
        foreach (var manifestPath in Directory.EnumerateFiles(
            channelRoot,
            ManifestFileName,
            SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = await TryReadManifestAsync(manifestPath, cancellationToken);
            if (manifest is null
                || manifest.SchemaVersion != ClientReleaseCatalogSchema.Version
                || !string.Equals(manifest.Channel, normalizedChannel, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(manifest.Version)
                || string.IsNullOrWhiteSpace(manifest.HostApiVersion)
                || string.IsNullOrWhiteSpace(manifest.TargetRuntime)
                || normalizedTargetRuntime is not null
                    && !string.Equals(manifest.TargetRuntime, normalizedTargetRuntime, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var artifactDirectory = Path.GetDirectoryName(manifestPath);
            if (string.IsNullOrWhiteSpace(artifactDirectory))
            {
                continue;
            }

            var hostRelease = TryBuildHostRelease(manifest, artifactDirectory);
            if (hostRelease is null)
            {
                continue;
            }

            hostReleases.Add(hostRelease);
            foreach (var pluginRelease in BuildPluginReleases(manifest, artifactDirectory))
            {
                pluginReleases.Add(pluginRelease);
            }
        }

        return new EdgeInstallerArtifactCatalogSnapshot(hostReleases, pluginReleases);
    }

    private string ResolveRootPath()
    {
        var rootPath = options.Value.RootPath;
        return Path.GetFullPath(rootPath);
    }

    private static async Task<InstallerArtifactManifest?> TryReadManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(manifestPath);
            return await JsonSerializer.DeserializeAsync<InstallerArtifactManifest>(
                stream,
                JsonOptions,
                cancellationToken);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ClientHostRelease? TryBuildHostRelease(
        InstallerArtifactManifest manifest,
        string artifactDirectory)
    {
        var sha256 = FirstNonEmpty(manifest.HostDirectorySha256, manifest.InstallerStubSha256);
        var packageSize = manifest.HostDirectorySize > 0
            ? manifest.HostDirectorySize
            : manifest.InstallerStubSize;
        if (string.IsNullOrWhiteSpace(sha256) || packageSize <= 0)
        {
            return null;
        }

        try
        {
            return new ClientHostRelease(
                manifest.Channel,
                manifest.Version,
                manifest.HostApiVersion,
                manifest.TargetRuntime,
                manifest.TargetFramework,
                BuildManifestDownloadUrl(manifest.Channel, manifest.Version),
                sha256,
                packageSize,
                null,
                ClientReleaseStatus.Published,
                null,
                "IIoT",
                ResolvePublishedAtUtc(manifest, artifactDirectory));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static IEnumerable<ClientPluginRelease> BuildPluginReleases(
        InstallerArtifactManifest manifest,
        string artifactDirectory)
    {
        var publishedAtUtc = ResolvePublishedAtUtc(manifest, artifactDirectory);
        foreach (var module in manifest.Modules)
        {
            if (string.IsNullOrWhiteSpace(module.ModuleId)
                || string.IsNullOrWhiteSpace(module.Version)
                || string.IsNullOrWhiteSpace(module.HostApiVersion)
                || string.IsNullOrWhiteSpace(module.MinHostVersion)
                || string.IsNullOrWhiteSpace(module.MaxHostVersion)
                || string.IsNullOrWhiteSpace(module.PluginSha256)
                || module.PluginSize <= 0)
            {
                continue;
            }

            ClientPluginRelease release;
            try
            {
                release = new ClientPluginRelease(
                    module.ModuleId,
                    string.IsNullOrWhiteSpace(module.DisplayName) ? module.ModuleId : module.DisplayName,
                    module.Description,
                    null,
                    null,
                    manifest.Channel,
                    module.Version,
                    module.HostApiVersion,
                    module.MinHostVersion,
                    module.MaxHostVersion,
                    manifest.TargetRuntime,
                    manifest.TargetFramework,
                    BuildPluginDownloadUrl(manifest.Channel, manifest.Version, module.ModuleId),
                    module.PluginSha256,
                    module.PluginSize,
                    null,
                    "[]",
                    ClientReleaseStatus.Published,
                    null,
                    "IIoT",
                    publishedAtUtc);
            }
            catch (ArgumentException)
            {
                continue;
            }

            yield return release;
        }
    }

    private static DateTime ResolvePublishedAtUtc(
        InstallerArtifactManifest manifest,
        string artifactDirectory)
    {
        if (manifest.GeneratedAtUtc is { } generatedAtUtc)
        {
            return generatedAtUtc.Kind == DateTimeKind.Utc
                ? generatedAtUtc
                : generatedAtUtc.ToUniversalTime();
        }

        return Directory.GetLastWriteTimeUtc(artifactDirectory);
    }

    private static string BuildManifestDownloadUrl(string channel, string version)
        => $"/edge-updates/installers/{Uri.EscapeDataString(channel)}/{Uri.EscapeDataString(version)}/{ManifestFileName}";

    private static string BuildPluginDownloadUrl(string channel, string version, string moduleId)
        => $"{BuildManifestDownloadUrl(channel, version)}#moduleId={Uri.EscapeDataString(moduleId)}";

    private static string NormalizeRequired(string value)
    {
        var normalized = value.Trim();
        ArgumentException.ThrowIfNullOrWhiteSpace(normalized);
        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.Select(NormalizeOptional).FirstOrDefault(value => value is not null);

    private static bool IsChildPath(string parentPath, string childPath)
    {
        var normalizedParent = Path.GetFullPath(parentPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedChild = Path.GetFullPath(childPath);
        return normalizedChild.StartsWith(
            normalizedParent + Path.DirectorySeparatorChar,
            StringComparison.Ordinal);
    }

    private sealed class InstallerArtifactManifest
    {
        public int SchemaVersion { get; set; }

        public string Channel { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;

        public string HostApiVersion { get; set; } = string.Empty;

        public string TargetRuntime { get; set; } = string.Empty;

        public string? TargetFramework { get; set; }

        public DateTime? GeneratedAtUtc { get; set; }

        public string? InstallerStubSha256 { get; set; }

        public long InstallerStubSize { get; set; }

        public string? HostDirectorySha256 { get; set; }

        public long HostDirectorySize { get; set; }

        public List<InstallerArtifactModule> Modules { get; set; } = [];
    }

    private sealed class InstallerArtifactModule
    {
        public string ModuleId { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string Version { get; set; } = string.Empty;

        public string HostApiVersion { get; set; } = string.Empty;

        public string MinHostVersion { get; set; } = string.Empty;

        public string MaxHostVersion { get; set; } = string.Empty;

        public string? PluginSha256 { get; set; }

        public long PluginSize { get; set; }
    }
}
