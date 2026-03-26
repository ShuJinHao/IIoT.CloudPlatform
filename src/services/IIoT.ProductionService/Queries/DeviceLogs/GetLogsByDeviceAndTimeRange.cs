using IIoT.Services.Common.Contracts.DapperQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.DeviceLogs;

/// <summary>
/// 日志查询四：设备号 + 时间范围
/// </summary>
public record GetLogsByDeviceAndTimeRangeQuery(
    Pagination PaginationParams,
    Guid DeviceId,
    DateTime StartTime,
    DateTime EndTime
) : IQuery<Result<object>>;

public class GetLogsByDeviceAndTimeRangeHandler(
    IDeviceLogQueryService queryService
) : IQueryHandler<GetLogsByDeviceAndTimeRangeQuery, Result<object>>
{
    public async Task<Result<object>> Handle(GetLogsByDeviceAndTimeRangeQuery request, CancellationToken cancellationToken)
    {
        if (request.StartTime >= request.EndTime)
            return Result.Failure("开始时间必须小于结束时间");

        var (items, totalCount) = await queryService.GetLogsByConditionAsync(
            request.PaginationParams,
            request.DeviceId,
            startTime: request.StartTime,
            endTime: request.EndTime,
            cancellationToken: cancellationToken);

        return Result.Success<object>(new
        {
            Items = items,
            MetaData = new
            {
                TotalCount = totalCount,
                PageSize = request.PaginationParams.PageSize,
                CurrentPage = request.PaginationParams.PageNumber,
                TotalPages = (int)Math.Ceiling(totalCount / (double)request.PaginationParams.PageSize)
            }
        });
    }
}