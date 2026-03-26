using IIoT.Services.Common.Contracts.DapperQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.PassStations.Injection;

/// <summary>
/// 详情查询：根据 ID 获取单条注液过站数据（含全部字段）
/// </summary>
public record GetInjectionDetailQuery(Guid Id) : IQuery<Result<object>>;

public class GetInjectionDetailHandler(
    IPassStationQueryService queryService
) : IQueryHandler<GetInjectionDetailQuery, Result<object>>
{
    public async Task<Result<object>> Handle(GetInjectionDetailQuery request, CancellationToken cancellationToken)
    {
        var detail = await queryService.GetInjectionDetailAsync(request.Id, cancellationToken);

        if (detail == null)
            return Result.Failure("未找到该过站记录");

        return Result.Success<object>(detail);
    }
}