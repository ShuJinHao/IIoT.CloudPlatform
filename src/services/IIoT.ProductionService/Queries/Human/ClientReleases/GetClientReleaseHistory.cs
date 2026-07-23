using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
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
    Guid ComponentId,
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
    IClientReleaseHistoryQueryService historyQueryService)
    : IQueryHandler<GetClientReleaseHistoryQuery, Result<PagedList<ClientReleaseHistoryComponentDto>>>
{
    public async Task<Result<PagedList<ClientReleaseHistoryComponentDto>>> Handle(
        GetClientReleaseHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var skip = (request.PaginationParams.PageNumber - 1) * request.PaginationParams.PageSize;
        var (history, totalCount) = await historyQueryService.GetPagedAsync(
            new ClientReleaseHistoryQueryRequest(
                request.Channel,
                request.TargetRuntime,
                skip,
                request.PaginationParams.PageSize),
            cancellationToken);

        var page = history
            .Select(item => new ClientReleaseHistoryComponentDto(
                item.ComponentId,
                item.ComponentKind,
                item.ComponentKey,
                item.DisplayName,
                item.Channel,
                item.TargetRuntime,
                item.Versions
                    .Select(version => new ClientReleaseHistoryVersionDto(
                        version.Id,
                        version.Version,
                        version.Status,
                        version.CreatedAtUtc,
                        version.PublishedAtUtc,
                        version.DeletedAtUtc,
                        version.DeletionReason,
                        version.DeletionFailure))
                    .ToList()))
            .ToList();

        return Result.Success(new PagedList<ClientReleaseHistoryComponentDto>(
            page,
            totalCount,
            request.PaginationParams));
    }
}
