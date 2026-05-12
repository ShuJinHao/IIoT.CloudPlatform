using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
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
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IDeviceLogQueryService queryService)
    : IQueryHandler<GetDeviceLogsQuery, Result<PagedList<DeviceLogListItemDto>>>
{
    public async Task<Result<PagedList<DeviceLogListItemDto>>> Handle(
        GetDeviceLogsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.DeviceId == Guid.Empty)
            return Result.Failure("设备不能为空");

        var deviceAccess = await currentUserDeviceAccessService.EnsureCanAccessDeviceAsync(
            request.DeviceId,
            cancellationToken);
        if (!deviceAccess.IsSuccess)
        {
            return Result.Failure(deviceAccess.Errors?.ToArray() ?? ["无权查看该设备日志"]);
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
