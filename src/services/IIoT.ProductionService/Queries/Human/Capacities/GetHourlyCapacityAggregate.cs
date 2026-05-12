using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Capacities;

[AuthorizeRequirement("Device.Read")]
public record GetHourlyCapacityAggregateQuery(
    DateOnly Date,
    Guid? ProcessId = null
) : IHumanQuery<Result<List<HourlyCapacityAggregateDto>>>;

public class GetHourlyCapacityAggregateHandler(
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    ICapacityQueryService queryService)
    : IQueryHandler<GetHourlyCapacityAggregateQuery, Result<List<HourlyCapacityAggregateDto>>>
{
    public async Task<Result<List<HourlyCapacityAggregateDto>>> Handle(
        GetHourlyCapacityAggregateQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await currentUserDeviceAccessService.GetAccessibleDeviceIdsAsync(cancellationToken);
        if (!scope.IsSuccess)
        {
            return Result.Failure(scope.Errors?.ToArray() ?? ["用户凭证异常"]);
        }

        if (scope.Value is { Count: 0 })
        {
            return Result.Success(new List<HourlyCapacityAggregateDto>());
        }

        var data = await queryService.GetHourlyAggregateAsync(
            request.Date,
            request.ProcessId,
            scope.Value,
            cancellationToken);

        return Result.Success(data);
    }
}
