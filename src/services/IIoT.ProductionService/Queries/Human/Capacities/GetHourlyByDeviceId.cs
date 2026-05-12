using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Capacities;

/// <summary>
/// 查询指定设备某日的小时产能明细
/// </summary>
[AuthorizeRequirement("Device.Read")]
public record GetHourlyByDeviceIdQuery(
    Guid DeviceId,
    DateOnly Date,
    string? PlcName = null
) : IHumanQuery<Result<List<HourlyCapacityDto>>>;

public class GetHourlyByDeviceIdHandler(
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    ICapacityQueryService queryService
) : IQueryHandler<GetHourlyByDeviceIdQuery, Result<List<HourlyCapacityDto>>>
{
    public async Task<Result<List<HourlyCapacityDto>>> Handle(
        GetHourlyByDeviceIdQuery request,
        CancellationToken cancellationToken)
    {
        var deviceAccess = await currentUserDeviceAccessService.EnsureCanAccessDeviceAsync(
            request.DeviceId,
            cancellationToken);
        if (!deviceAccess.IsSuccess)
        {
            return Result.Failure(deviceAccess.Errors?.ToArray() ?? ["无权查看该设备的小时产能"]);
        }

        var data = await queryService.GetHourlyByDeviceIdAsync(
            request.DeviceId,
            request.Date,
            request.PlcName,
            cancellationToken);

        return Result.Success(data);
    }
}
