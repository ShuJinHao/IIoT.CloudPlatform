using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.DapperQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.PassStations.Injection;

/// <summary>
/// 追溯查询一：条码 + 工序精确查询
/// </summary>
public record GetInjectionByBarcodeAndProcessQuery(
    Pagination PaginationParams,
    Guid ProcessId,
    string Barcode
) : IQuery<Result<object>>;

public class GetInjectionByBarcodeAndProcessHandler(
    IDataQueryService dataQueryService,
    IPassStationQueryService queryService
) : IQueryHandler<GetInjectionByBarcodeAndProcessQuery, Result<object>>
{
    public async Task<Result<object>> Handle(GetInjectionByBarcodeAndProcessQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Barcode))
            return Result.Failure("条码不能为空");

        var devices = await dataQueryService.ToListAsync(
            dataQueryService.Devices.Where(d => d.ProcessId == request.ProcessId && d.IsActive));

        if (devices.Count == 0)
            return Result.Failure("该工序下没有活跃设备");

        var deviceIds = devices.Select(d => d.Id).ToList();

        var (items, totalCount) = await queryService.GetInjectionByConditionAsync(
            request.PaginationParams,
            deviceIds: deviceIds,
            barcode: request.Barcode,
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