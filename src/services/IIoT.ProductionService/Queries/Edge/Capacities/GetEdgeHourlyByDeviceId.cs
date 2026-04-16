using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Capacities;

public record GetEdgeHourlyByDeviceIdQuery(
    Guid DeviceId,
    DateOnly Date,
    string? PlcName = null
) : IDeviceQuery<Result<List<HourlyCapacityDto>>>;

public class GetEdgeHourlyByDeviceIdHandler(
    ICapacityQueryService queryService
) : IQueryHandler<GetEdgeHourlyByDeviceIdQuery, Result<List<HourlyCapacityDto>>>
{
    public async Task<Result<List<HourlyCapacityDto>>> Handle(
        GetEdgeHourlyByDeviceIdQuery request,
        CancellationToken cancellationToken)
    {
        var data = await queryService.GetHourlyByDeviceIdAsync(
            request.DeviceId,
            request.Date,
            request.PlcName,
            cancellationToken);

        return Result.Success(data);
    }
}
