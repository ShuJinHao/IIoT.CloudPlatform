using IIoT.Services.Common.Contracts.DapperQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.PassStations.Injection;

/// <summary>
/// 追溯查询三：设备号 + 条码精确查询
/// </summary>
public record GetInjectionByDeviceAndBarcodeQuery(
    Pagination PaginationParams,
    Guid DeviceId,
    string Barcode
) : IQuery<Result<object>>;

public class GetInjectionByDeviceAndBarcodeHandler(
    IPassStationQueryService queryService
) : IQueryHandler<GetInjectionByDeviceAndBarcodeQuery, Result<object>>
{
    public async Task<Result<object>> Handle(GetInjectionByDeviceAndBarcodeQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Barcode))
            return Result.Failure("条码不能为空");

        var (items, totalCount) = await queryService.GetInjectionByConditionAsync(
            request.PaginationParams,
            deviceId: request.DeviceId,
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