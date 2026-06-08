using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.ClientReleases;

[AuthorizeRequirement("Device.Read")]
public sealed record GetClientReleaseCatalogQuery(
    string? Channel = null,
    string? TargetRuntime = null,
    bool OnlyPublished = false) : IHumanQuery<Result<ClientReleaseCatalogDto>>;

public sealed class GetClientReleaseCatalogHandler(
    IReadRepository<ClientHostRelease> hostReleaseRepository,
    IReadRepository<ClientPluginRelease> pluginReleaseRepository)
    : IQueryHandler<GetClientReleaseCatalogQuery, Result<ClientReleaseCatalogDto>>
{
    public async Task<Result<ClientReleaseCatalogDto>> Handle(
        GetClientReleaseCatalogQuery request,
        CancellationToken cancellationToken)
    {
        var channel = NormalizeChannel(request.Channel);
        var hostReleases = await hostReleaseRepository.GetListAsync(
            new ClientHostReleasesByChannelSpec(channel, request.TargetRuntime, request.OnlyPublished),
            cancellationToken);
        var pluginReleases = await pluginReleaseRepository.GetListAsync(
            new ClientPluginReleasesByChannelSpec(channel, request.TargetRuntime, request.OnlyPublished),
            cancellationToken);

        var orderedHosts = hostReleases
            .OrderByDescending(release => release.Status == ClientReleaseStatus.Published)
            .ThenByDescending(release => release.PublishedAtUtc ?? release.CreatedAtUtc)
            .ThenByDescending(release => release.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var orderedPlugins = pluginReleases
            .OrderBy(release => release.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(release => release.Status == ClientReleaseStatus.Published)
            .ThenByDescending(release => release.PublishedAtUtc ?? release.CreatedAtUtc)
            .ThenByDescending(release => release.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var latestHost = orderedHosts
            .Where(release => release.Status == ClientReleaseStatus.Published)
            .OrderByDescending(release => release, ClientHostReleaseVersionComparer.Instance)
            .FirstOrDefault();

        return Result.Success(new ClientReleaseCatalogDto(
            ClientReleaseCatalogSchema.Version,
            channel,
            NormalizeOptional(request.TargetRuntime),
            latestHost is null ? null : ClientReleaseMapping.ToDto(latestHost),
            orderedHosts.Select(ClientReleaseMapping.ToDto).ToList(),
            orderedPlugins.Select(ClientReleaseMapping.ToDto).ToList(),
            DateTime.UtcNow));
    }

    private static string NormalizeChannel(string? channel)
    {
        return string.IsNullOrWhiteSpace(channel) ? "stable" : channel.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
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
}
