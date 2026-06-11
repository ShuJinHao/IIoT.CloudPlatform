using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.ClientReleases;

public sealed record GetPublicClientDownloadsQuery(
    string? Channel = null,
    string? TargetRuntime = null) : IPublicQuery<Result<PublicClientDownloadCatalogDto>>;

public sealed class GetPublicClientDownloadsHandler(
    IReadRepository<ClientHostRelease> hostReleaseRepository,
    IReadRepository<ClientPluginRelease> pluginReleaseRepository)
    : IQueryHandler<GetPublicClientDownloadsQuery, Result<PublicClientDownloadCatalogDto>>
{
    public async Task<Result<PublicClientDownloadCatalogDto>> Handle(
        GetPublicClientDownloadsQuery request,
        CancellationToken cancellationToken)
    {
        var channel = Normalize(request.Channel, "stable");
        var targetRuntime = Normalize(request.TargetRuntime, "win-x64");
        var hostReleases = await hostReleaseRepository.GetListAsync(
            new ClientHostReleasesByChannelSpec(channel, targetRuntime, onlyPublished: true),
            cancellationToken);
        var pluginReleases = await pluginReleaseRepository.GetListAsync(
            new ClientPluginReleasesByChannelSpec(channel, targetRuntime, onlyPublished: true),
            cancellationToken);

        var latestHost = hostReleases
            .OrderByDescending(release => release, ClientHostReleaseVersionComparer.Instance)
            .FirstOrDefault();
        var plugins = pluginReleases
            .GroupBy(release => release.ModuleId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(release => release, ClientPluginReleaseVersionComparer.Instance)
                .First())
            .OrderBy(release => release.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(release => release.ModuleId, StringComparer.OrdinalIgnoreCase)
            .Select(ToPublicPlugin)
            .ToList();

        return Result.Success(new PublicClientDownloadCatalogDto(
            ClientReleaseCatalogSchema.Version,
            channel,
            targetRuntime,
            latestHost is null ? null : ToPublicHost(latestHost),
            plugins,
            DateTime.UtcNow));
    }

    private static string Normalize(string? value, string fallback)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static PublicClientHostDownloadDto ToPublicHost(ClientHostRelease release)
    {
        return new PublicClientHostDownloadDto(
            release.Channel,
            release.Version,
            release.HostApiVersion,
            release.TargetRuntime,
            release.TargetFramework,
            release.Sha256,
            release.PackageSize,
            release.ReleaseNotes,
            release.Publisher,
            release.PublishedAtUtc);
    }

    private static PublicClientPluginCatalogItemDto ToPublicPlugin(ClientPluginRelease release)
    {
        return new PublicClientPluginCatalogItemDto(
            release.ModuleId,
            release.DisplayName,
            release.Description,
            release.IconKind,
            release.AccentColor,
            release.Channel,
            release.Version,
            release.HostApiVersion,
            release.MinHostVersion,
            release.MaxHostVersion,
            release.TargetRuntime,
            release.TargetFramework,
            release.PackageSize,
            release.ReleaseNotes,
            ClientReleaseMapping.ParseDependencies(release.DependenciesJson),
            release.Publisher,
            release.PublishedAtUtc);
    }

    private sealed class ClientHostReleaseVersionComparer : IComparer<ClientHostRelease>
    {
        public static ClientHostReleaseVersionComparer Instance { get; } = new();

        public int Compare(ClientHostRelease? x, ClientHostRelease? y)
        {
            if (x is null && y is null)
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var versionCompare = ClientReleaseMapping.CompareVersions(x.Version, y.Version);
            if (versionCompare != 0)
            {
                return versionCompare;
            }

            return DateTime.Compare(x.PublishedAtUtc ?? x.CreatedAtUtc, y.PublishedAtUtc ?? y.CreatedAtUtc);
        }
    }

    private sealed class ClientPluginReleaseVersionComparer : IComparer<ClientPluginRelease>
    {
        public static ClientPluginReleaseVersionComparer Instance { get; } = new();

        public int Compare(ClientPluginRelease? x, ClientPluginRelease? y)
        {
            if (x is null && y is null)
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var versionCompare = ClientReleaseMapping.CompareVersions(x.Version, y.Version);
            if (versionCompare != 0)
            {
                return versionCompare;
            }

            return DateTime.Compare(x.PublishedAtUtc ?? x.CreatedAtUtc, y.PublishedAtUtc ?? y.CreatedAtUtc);
        }
    }
}
