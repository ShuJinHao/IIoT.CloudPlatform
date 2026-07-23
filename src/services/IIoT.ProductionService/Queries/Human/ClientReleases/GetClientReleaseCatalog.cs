using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.Queries.ClientReleases;

/// <summary>
/// Human 发布管理主 catalog：只返回活动版本（Draft/Published/Deprecated），
/// 归档、已删除和待删除版本不属于主 catalog，历史由独立历史查询承载。
/// spec 默认排除 Archived/Deleted/DeleteRequested/DeleteFailed，没有活动版本的组件自然不返回。
/// </summary>
[AuthorizeRequirement(ClientReleasePermissions.Read)]
public sealed record GetClientReleaseCatalogQuery(
    string? Channel = null,
    string? TargetRuntime = null,
    bool OnlyPublished = false) : IHumanQuery<Result<ClientReleaseCatalogDto>>;

public sealed class GetClientReleaseCatalogHandler(
    IReadRepository<ClientReleaseComponent> componentRepository,
    IClientReleaseComponentDeletionStore deletionStore,
    IOptions<EdgeInstallerArtifactOptions> artifactOptions)
    : IQueryHandler<GetClientReleaseCatalogQuery, Result<ClientReleaseCatalogDto>>
{
    public async Task<Result<ClientReleaseCatalogDto>> Handle(
        GetClientReleaseCatalogQuery request,
        CancellationToken cancellationToken)
    {
        var channel = ClientReleaseText.NormalizeChannel(request.Channel);
        var components = await componentRepository.GetListAsync(
            new ClientReleaseComponentsByChannelSpec(
                channel,
                request.TargetRuntime,
                request.OnlyPublished),
            cancellationToken);

        var edgeRoot = artifactOptions.Value.ResolveEdgeUpdatesRoot();
        var missingPaths = ClientReleaseMissingFiles.Collect(edgeRoot, components);
        // 待清理的永久删除操作仍持有精确文件目标；同 channel 版本在这些路径收敛前不得当作可分发。
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
                onlyPublished: request.OnlyPublished),
            ClientReleaseMapping.ToPluginComponentsExcludingMissingFiles(
                components,
                missingPaths,
                onlyPublished: request.OnlyPublished),
            DateTime.UtcNow));
    }

}
