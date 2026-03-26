using IIoT.Services.Common.Contracts.DapperQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Capacities;

/// <summary>
/// 查询：所有机台产能分页加载（延迟加载，带设备名称和良率）
/// </summary>
public record GetDailyCapacityPagedQuery(
    Pagination PaginationParams,
    DateOnly? Date = null,
    Guid? DeviceId = null
) : IQuery<Result<object>>;

public class GetDailyCapacityPagedHandler(
    ICapacityQueryService queryService
) : IQueryHandler<GetDailyCapacityPagedQuery, Result<object>>
{
    public async Task<Result<object>> Handle(GetDailyCapacityPagedQuery request, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await queryService.GetDailyPagedAsync(
            request.PaginationParams,
            request.Date,
            request.DeviceId,
            cancellationToken);

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