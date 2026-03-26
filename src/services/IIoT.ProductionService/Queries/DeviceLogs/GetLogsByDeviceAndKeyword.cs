using IIoT.Services.Common.Contracts.DapperQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.DeviceLogs;

/// <summary>
/// 日志查询二：设备号 + 模糊搜索
/// </summary>
public record GetLogsByDeviceAndKeywordQuery(
    Pagination PaginationParams,
    Guid DeviceId,
    string Keyword
) : IQuery<Result<object>>;

public class GetLogsByDeviceAndKeywordHandler(
    IDeviceLogQueryService queryService
) : IQueryHandler<GetLogsByDeviceAndKeywordQuery, Result<object>>
{
    public async Task<Result<object>> Handle(GetLogsByDeviceAndKeywordQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Keyword))
            return Result.Failure("搜索关键字不能为空");

        var (items, totalCount) = await queryService.GetLogsByConditionAsync(
            request.PaginationParams,
            request.DeviceId,
            keyword: request.Keyword,
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