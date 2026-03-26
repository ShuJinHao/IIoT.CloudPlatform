using IIoT.Services.Common.Contracts.DapperQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.DeviceLogs;

/// <summary>
/// 日志查询三：设备号 + 指定日期（查某一天的完整日志）
/// </summary>
public record GetLogsByDeviceAndDateQuery(
    Pagination PaginationParams,
    Guid DeviceId,
    DateOnly Date
) : IQuery<Result<object>>;

public class GetLogsByDeviceAndDateHandler(
    IDeviceLogQueryService queryService
) : IQueryHandler<GetLogsByDeviceAndDateQuery, Result<object>>
{
    public async Task<Result<object>> Handle(GetLogsByDeviceAndDateQuery request, CancellationToken cancellationToken)
    {
        var startTime = request.Date.ToDateTime(TimeOnly.MinValue);
        var endTime = request.Date.ToDateTime(new TimeOnly(23, 59, 59));

        var (items, totalCount) = await queryService.GetLogsByConditionAsync(
            request.PaginationParams,
            request.DeviceId,
            startTime: startTime,
            endTime: endTime,
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