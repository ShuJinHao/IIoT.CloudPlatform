using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.Queries.ClientReleases;

public sealed record GetEdgeClientReleaseCatalogQuery(
    Guid DeviceId,
    string? Channel = null,
    string? TargetRuntime = null) : IDeviceQuery<Result<ClientReleaseCatalogDto>>;

public sealed class GetEdgeClientReleaseCatalogHandler(
    IDeviceIdentityQueryService deviceIdentityQueryService,
    IReadRepository<ClientHostRelease> hostReleaseRepository,
    IReadRepository<ClientPluginRelease> pluginReleaseRepository,
    IClientReleaseRetentionPolicyReader retentionPolicyReader,
    IOptions<EdgeInstallerArtifactOptions> artifactOptions,
    IEdgeInstallerArtifactCatalogReader artifactCatalogReader)
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
        var databaseHostReleases = await hostReleaseRepository.GetListAsync(
            new ClientHostReleasesByChannelSpec(channel, request.TargetRuntime, onlyPublished: false, includeArchived: true),
            cancellationToken);
        var databasePluginReleases = await pluginReleaseRepository.GetListAsync(
            new ClientPluginReleasesByChannelSpec(channel, request.TargetRuntime, onlyPublished: false, includeArchived: true),
            cancellationToken);
        var artifactCatalog = await artifactCatalogReader.ReadAsync(channel, request.TargetRuntime, cancellationToken);
        var hostReleases = ClientReleaseCatalogMerge.MergeHostReleases(
            databaseHostReleases,
            artifactCatalog.HostReleases,
            onlyPublished: true);
        var pluginReleases = ClientReleaseCatalogMerge.MergePluginReleases(
            databasePluginReleases,
            artifactCatalog.PluginReleases,
            onlyPublished: true);
        var maxVersions = await retentionPolicyReader.GetMaxVersionsPerComponentAsync(cancellationToken);

        return Result.Success(new ClientReleaseCatalogDto(
            ClientReleaseCatalogSchema.Version,
            channel,
            NormalizeOptional(request.TargetRuntime),
            ClientReleaseMapping.ToHostComponent(hostReleases, maxVersions),
            ClientReleaseMapping.ToPluginComponents(pluginReleases, maxVersions),
            DateTime.UtcNow,
            artifactOptions.Value.BuildVelopackUpdateSource(channel)));
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
