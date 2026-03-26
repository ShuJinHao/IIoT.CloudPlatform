using IIoT.Services.Common.Contracts.DapperQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.DeviceLogs;

/// <summary>
/// 日志查询五：设备号 + 日期 + 模糊搜索（最精确定位）
/// </summary>
public record GetLogsByDeviceDateAndKeywordQuery(
    Pagination PaginationParams,
    Guid DeviceId,
    DateOnly Date,
    string Keyword
) : IQuery<Result<object>>;

public class GetLogsByDeviceDateAndKeywordHandler(
    IDeviceLogQueryService queryService
) : IQueryHandler<GetLogsByDeviceDateAndKeywordQuery, Result<object>>
{
    public async Task<Result<object>> Handle(GetLogsByDeviceDateAndKeywordQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Keyword))
            return Result.Failure("搜索关键字不能为空");

        var startTime = request.Date.ToDateTime(TimeOnly.MinValue);
        var endTime = request.Date.ToDateTime(new TimeOnly(23, 59, 59));

        var (items, totalCount) = await queryService.GetLogsByConditionAsync(
            request.PaginationParams,
            request.DeviceId,
            keyword: request.Keyword,
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