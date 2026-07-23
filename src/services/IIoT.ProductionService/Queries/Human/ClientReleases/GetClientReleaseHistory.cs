using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.ClientReleases;

/// <summary>
/// 发布历史独立查询：只读 Archived/Deleted/DeleteFailed 版本，附删除时间、删除原因和失败原因。
/// 历史与主 catalog 严格分离，不把历史版本混回主列表；分页/计数在数据库执行。
/// </summary>
[AuthorizeRequirement(ClientReleasePermissions.Read)]
public sealed record GetClientReleaseHistoryQuery(
    Pagination PaginationParams,
    string? Channel = null,
    string? TargetRuntime = null) : IHumanQuery<Result<PagedList<ClientReleaseHistoryComponentDto>>>;

public sealed record ClientReleaseHistoryComponentDto(
    string ComponentKind,
    string ModuleId,
    string DisplayName,
    string Channel,
    string TargetRuntime,
    IReadOnlyList<ClientReleaseHistoryVersionDto> Versions);

public sealed record ClientReleaseHistoryVersionDto(
    Guid Id,
    string Version,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? PublishedAtUtc,
    DateTime? DeletedAtUtc,
    string? DeletionReason,
    string? DeletionFailure);

public sealed class GetClientReleaseHistoryHandler(
    IReadRepository<ClientReleaseComponent> componentRepository)
    : IQueryHandler<GetClientReleaseHistoryQuery, Result<PagedList<ClientReleaseHistoryComponentDto>>>
{
    public async Task<Result<PagedList<ClientReleaseHistoryComponentDto>>> Handle(
        GetClientReleaseHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var channel = string.IsNullOrWhiteSpace(request.Channel) ? null : request.Channel.Trim();
        var components = await componentRepository.GetListAsync(
            new ClientReleaseComponentsByChannelSpec(channel, request.TargetRuntime, onlyPublished: false, includeArchived: true),
            cancellationToken);

        // 只保留有历史版本的组件，版本集合收窄为历史状态。
        var history = components
            .Select(component => (Component: component, Versions: component.Versions
                .Where(version => version.Status is ClientReleaseStatus.Archived
                    or ClientReleaseStatus.Deleted
                    or ClientReleaseStatus.DeleteFailed)
                .OrderByDescending(version => version.DeletedAtUtc ?? version.PublishedAtUtc ?? version.CreatedAtUtc)
                .ToList()))
            .Where(item => item.Versions.Count > 0)
            .OrderByDescending(item => item.Versions[0].DeletedAtUtc ?? item.Versions[0].PublishedAtUtc ?? item.Versions[0].CreatedAtUtc)
            .ToList();

        var skip = (request.PaginationParams.PageNumber - 1) * request.PaginationParams.PageSize;
        var page = history
            .Skip(skip)
            .Take(request.PaginationParams.PageSize)
            .Select(item => new ClientReleaseHistoryComponentDto(
                item.Component.ComponentKind.ToString(),
                item.Component.ComponentKey,
                item.Component.DisplayName,
                item.Component.Channel,
                item.Component.TargetRuntime,
                item.Versions
                    .Select(version => new ClientReleaseHistoryVersionDto(
                        version.Id,
                        version.Version,
                        version.Status.ToString(),
                        version.CreatedAtUtc,
                        version.PublishedAtUtc,
                        version.DeletedAtUtc,
                        version.DeletionReason,
                        version.DeletionFailure))
                    .ToList()))
            .ToList();

        return Result.Success(new PagedList<ClientReleaseHistoryComponentDto>(
            page,
            history.Count,
            request.PaginationParams));
    }
}
