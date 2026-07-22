using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
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
    IReadRepository<ClientReleaseComponent> componentRepository,
    IClientReleaseRetentionPolicyReader retentionPolicyReader,
    IClientReleaseComponentDeletionStore deletionStore,
    IOptions<EdgeInstallerArtifactOptions> artifactOptions)
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

        var channel = ClientReleaseText.NormalizeChannel(request.Channel);
        var components = await componentRepository.GetListAsync(
            new ClientReleaseComponentsByChannelSpec(channel, request.TargetRuntime, onlyPublished: true),
            cancellationToken);
        var maxVersions = await retentionPolicyReader.GetMaxVersionsPerComponentAsync(cancellationToken);
        var edgeRoot = artifactOptions.Value.ResolveEdgeUpdatesRoot();
        var missingPaths = ClientReleaseMissingFiles.Collect(edgeRoot, components);
        foreach (var deletion in await deletionStore.GetByChannelAsync(channel, cancellationToken))
        {
            foreach (var file in deletion.Files)
            {
                missingPaths.Add(file.RelativePath);
            }
        }

        return Result.Success(new ClientReleaseCatalogDto(
            ClientReleaseCatalogSchema.Version,
            channel,
            ClientReleaseText.NormalizeOptional(request.TargetRuntime),
            ClientReleaseMapping.ToHostComponentExcludingMissingFiles(
                components,
                missingPaths,
                maxVersions,
                onlyPublished: true),
            ClientReleaseMapping.ToPluginComponentsExcludingMissingFiles(
                components,
                missingPaths,
                maxVersions,
                onlyPublished: true),
            DateTime.UtcNow,
            artifactOptions.Value.BuildVelopackUpdateSource(channel)));
    }

}
