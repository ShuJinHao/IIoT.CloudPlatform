using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.DapperQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.PassStations.Injection;

/// <summary>
/// 追溯查询二：时间范围 + 工序精确查询
/// </summary>
public record GetInjectionByTimeAndProcessQuery(
    Pagination PaginationParams,
    Guid ProcessId,
    DateTime StartTime,
    DateTime EndTime
) : IQuery<Result<object>>;

public class GetInjectionByTimeAndProcessHandler(
    IDataQueryService dataQueryService,
    IPassStationQueryService queryService
) : IQueryHandler<GetInjectionByTimeAndProcessQuery, Result<object>>
{
    public async Task<Result<object>> Handle(GetInjectionByTimeAndProcessQuery request, CancellationToken cancellationToken)
    {
        if (request.StartTime >= request.EndTime)
            return Result.Failure("开始时间必须小于结束时间");

        var devices = await dataQueryService.ToListAsync(
            dataQueryService.Devices.Where(d => d.ProcessId == request.ProcessId && d.IsActive));

        if (devices.Count == 0)
            return Result.Failure("该工序下没有活跃设备");

        var deviceIds = devices.Select(d => d.Id).ToList();

        var (items, totalCount) = await queryService.GetInjectionByConditionAsync(
            request.PaginationParams,
            deviceIds: deviceIds,
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