using IIoT.Services.Common.Contracts.DapperQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.PassStations.Injection;

/// <summary>
/// 追溯查询四：设备号 + 时间范围精确查询
/// </summary>
public record GetInjectionByDeviceAndTimeQuery(
    Pagination PaginationParams,
    Guid DeviceId,
    DateTime StartTime,
    DateTime EndTime
) : IQuery<Result<object>>;

public class GetInjectionByDeviceAndTimeHandler(
    IPassStationQueryService queryService
) : IQueryHandler<GetInjectionByDeviceAndTimeQuery, Result<object>>
{
    public async Task<Result<object>> Handle(GetInjectionByDeviceAndTimeQuery request, CancellationToken cancellationToken)
    {
        if (request.StartTime >= request.EndTime)
            return Result.Failure("开始时间必须小于结束时间");

        var (items, totalCount) = await queryService.GetInjectionByConditionAsync(
            request.PaginationParams,
            deviceId: request.DeviceId,
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