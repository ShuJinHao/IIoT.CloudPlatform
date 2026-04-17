using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.Authorization;
using IIoT.Services.Common.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.DeviceLogs;

[AuthorizeRequirement("Device.Read")]
public record GetDeviceLogsQuery(
    Pagination PaginationParams,
    Guid DeviceId,
    string? Level = null,
    string? Keyword = null,
    DateTime? StartTime = null,
    DateTime? EndTime = null
) : IHumanQuery<Result<PagedList<DeviceLogListItemDto>>>;

public class GetDeviceLogsHandler(
    ICurrentUser currentUser,
    IDevicePermissionService devicePermissionService,
    IDeviceLogQueryService queryService)
    : IQueryHandler<GetDeviceLogsQuery, Result<PagedList<DeviceLogListItemDto>>>
{
    public async Task<Result<PagedList<DeviceLogListItemDto>>> Handle(
        GetDeviceLogsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.DeviceId == Guid.Empty)
            return Result.Failure("DeviceId 不能为空");

        if (!string.Equals(
                currentUser.Role,
                IIoT.Services.Common.Contracts.Authorization.SystemRoles.Admin,
                StringComparison.Ordinal))
        {
            if (!Guid.TryParse(currentUser.Id, out var userId))
                return Result.Failure("用户凭证异常");

            var accessibleDeviceIds = await devicePermissionService.GetAccessibleDeviceIdsAsync(
                userId,
                isAdmin: false,
                cancellationToken);
            if (accessibleDeviceIds is null || !accessibleDeviceIds.Contains(request.DeviceId))
                return Result.Failure("无权查看该设备日志");
        }

        var (items, totalCount) = await queryService.GetLogsByConditionAsync(
            request.PaginationParams,
            request.DeviceId,
            request.Level,
            request.Keyword,
            request.StartTime,
            request.EndTime,
            cancellationToken);

        var pagedList = new PagedList<DeviceLogListItemDto>(
            items, totalCount, request.PaginationParams);

        return Result.Success(pagedList);
    }
}
