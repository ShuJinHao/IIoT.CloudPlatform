using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Devices;

[AuthorizeRequirement("Device.Read")]
public record GetDeviceStatusSummaryQuery() : IHumanQuery<Result<DeviceStatusSummaryDto>>;

public class GetDeviceStatusSummaryHandler(
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IDeviceOperationalStatusQueryService queryService)
    : IQueryHandler<GetDeviceStatusSummaryQuery, Result<DeviceStatusSummaryDto>>
{
    private const int OfflineThresholdMinutes = 60;
    private const int StatusLogWindowHours = 24;

    public async Task<Result<DeviceStatusSummaryDto>> Handle(
        GetDeviceStatusSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var allowedDeviceIds = await currentUserDeviceAccessService.GetAccessibleDeviceIdsAsync(cancellationToken);
        if (!allowedDeviceIds.IsSuccess)
        {
            return Result.Failure(allowedDeviceIds.Errors?.ToArray() ?? ["用户凭证异常"]);
        }

        if (allowedDeviceIds.Value is { Count: 0 })
        {
            return Result.Success(new DeviceStatusSummaryDto(0, 0, 0, 0, 0, DateTimeOffset.UtcNow));
        }

        var now = DateTimeOffset.UtcNow;
        var summary = await queryService.GetStatusSummaryAsync(
            now.AddMinutes(-OfflineThresholdMinutes),
            now.AddHours(-StatusLogWindowHours),
            allowedDeviceIds.Value,
            cancellationToken);

        return Result.Success(summary);
    }
}
