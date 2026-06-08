using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.ClientReleases;

public sealed record GetEdgeClientReleaseCatalogQuery(
    Guid DeviceId,
    string? Channel = null,
    string? TargetRuntime = null) : IDeviceQuery<Result<ClientReleaseCatalogDto>>;

public sealed class GetEdgeClientReleaseCatalogHandler(
    IDeviceIdentityQueryService deviceIdentityQueryService,
    IReadRepository<ClientHostRelease> hostReleaseRepository,
    IReadRepository<ClientPluginRelease> pluginReleaseRepository)
    : IQueryHandler<GetEdgeClientReleaseCatalogQuery, Result<ClientReleaseCatalogDto>>
{
    public async Task<Result<ClientReleaseCatalogDto>> Handle(
        GetEdgeClientReleaseCatalogQuery request,
        CancellationToken cancellationToken)
    {
        var identity = await deviceIdentityQueryService.GetByDeviceIdAsync(
            request.DeviceId,
            cancellationToken);
        if (identity is null)
        {
            return Result.Failure("Catalog 查询失败: 设备不存在");
        }

        var channel = string.IsNullOrWhiteSpace(request.Channel) ? "stable" : request.Channel.Trim();
        var hostReleases = await hostReleaseRepository.GetListAsync(
            new ClientHostReleasesByChannelSpec(channel, request.TargetRuntime, onlyPublished: true),
            cancellationToken);
        var pluginReleases = await pluginReleaseRepository.GetListAsync(
            new ClientPluginReleasesByChannelSpec(channel, request.TargetRuntime, onlyPublished: true),
            cancellationToken);

        var latestHost = hostReleases
            .OrderByDescending(release => release.Version, VersionStringComparer.Instance)
            .ThenByDescending(release => release.PublishedAtUtc ?? release.CreatedAtUtc)
            .FirstOrDefault();

        return Result.Success(new ClientReleaseCatalogDto(
            ClientReleaseCatalogSchema.Version,
            channel,
            NormalizeOptional(request.TargetRuntime),
            latestHost is null ? null : ClientReleaseMapping.ToDto(latestHost),
            hostReleases
                .OrderByDescending(release => release.Version, VersionStringComparer.Instance)
                .ThenByDescending(release => release.PublishedAtUtc ?? release.CreatedAtUtc)
                .Select(ClientReleaseMapping.ToDto)
                .ToList(),
            pluginReleases
                .OrderBy(release => release.ModuleId, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(release => release.Version, VersionStringComparer.Instance)
                .ThenByDescending(release => release.PublishedAtUtc ?? release.CreatedAtUtc)
                .Select(ClientReleaseMapping.ToDto)
                .ToList(),
            DateTime.UtcNow));
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private sealed class VersionStringComparer : IComparer<string>
    {
        public static VersionStringComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            return ClientReleaseMapping.CompareVersions(x, y);
        }
    }
}
