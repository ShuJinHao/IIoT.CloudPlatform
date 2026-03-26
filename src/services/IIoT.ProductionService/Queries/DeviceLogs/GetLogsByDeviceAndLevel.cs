using IIoT.Services.Common.Contracts.DapperQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.DeviceLogs;

/// <summary>
/// 日志查询一：设备号 + 日志级别筛选
/// </summary>
public record GetLogsByDeviceAndLevelQuery(
    Pagination PaginationParams,
    Guid DeviceId,
    string? Level = null
) : IQuery<Result<object>>;

public class GetLogsByDeviceAndLevelHandler(
    IDeviceLogQueryService queryService
) : IQueryHandler<GetLogsByDeviceAndLevelQuery, Result<object>>
{
    public async Task<Result<object>> Handle(GetLogsByDeviceAndLevelQuery request, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await queryService.GetLogsByConditionAsync(
            request.PaginationParams,
            request.DeviceId,
            level: request.Level,
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