using AutoMapper;
using IIoT.ProductionService.Commands;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Events.Capacities;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.Contracts.Uploads;
using IIoT.Services.CrossCutting.Caching;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Capacities;

public record ReceiveHourlyCapacityCommand(
    Guid DeviceId,
    DateOnly Date,
    string ShiftCode,
    int Hour,
    int Minute,
    string TimeLabel,
    int TotalCount,
    int OkCount,
    int NgCount,
    string? PlcName = null,
    string? RequestId = null
) : IDeviceCommand<Result<EdgeUploadAcceptedResponse>>;

public class ReceiveHourlyCapacityHandler(
    IDeviceIdentityQueryService deviceIdentityQuery,
    IMapper mapper,
    IUploadReceiveRegistry uploadReceiveRegistry,
    ICacheService cacheService
) : ICommandHandler<ReceiveHourlyCapacityCommand, Result<EdgeUploadAcceptedResponse>>
{
    public async Task<Result<EdgeUploadAcceptedResponse>> Handle(
        ReceiveHourlyCapacityCommand request,
        CancellationToken cancellationToken)
    {
        if (request.DeviceId == Guid.Empty)
            return Result.Failure("数据接收失败: DeviceId 不能为空");

        var exists = await deviceIdentityQuery.ExistsAsync(request.DeviceId, cancellationToken);
        if (!exists)
            return Result.Failure("数据接收失败: 设备不存在");

        var deduplicationKey = UploadDeduplicationKeys.ForHourlyCapacity(request);
        if (!deduplicationKey.IsSuccess)
            return Result.Failure(deduplicationKey.Errors?.ToArray() ?? []);

        var @event = mapper.Map<HourlyCapacityReceivedEvent>(request) with
        {
            ReceivedAtUtc = DateTime.UtcNow
        };
        var registration = await uploadReceiveRegistry.RegisterAndEnqueueAsync(
            request.DeviceId,
            UploadMessageTypes.HourlyCapacity,
            UploadDeduplicationKeys.NormalizeRequestId(request.RequestId),
            deduplicationKey.Value!,
            @event,
            cancellationToken);
        if (registration.IsDuplicate)
            return Result.Success(EdgeUploadAcceptedResponse.Duplicate(registration.OutboxMessageId));

        await cacheService.RemoveAsync(
            CacheKeys.CapacityHourly(request.DeviceId, request.Date, request.PlcName),
            cancellationToken);
        await cacheService.RemoveAsync(
            CacheKeys.CapacitySummary(request.DeviceId, request.Date, request.PlcName),
            cancellationToken);
        await cacheService.RemoveAsync(
            CacheKeys.CapacityRange(request.DeviceId, request.Date, request.Date, request.PlcName),
            cancellationToken);
        await cacheService.RemoveByPatternAsync(
            CacheKeys.CapacityHourlyPattern(request.DeviceId),
            cancellationToken);
        await cacheService.RemoveByPatternAsync(
            CacheKeys.CapacitySummaryPattern(request.DeviceId),
            cancellationToken);
        await cacheService.RemoveByPatternAsync(
            CacheKeys.CapacityRangePattern(request.DeviceId),
            cancellationToken);
        await cacheService.RemoveByPatternAsync(
            CacheKeys.CapacityPagedByDevicePattern(request.DeviceId),
            cancellationToken);

        return Result.Success(EdgeUploadAcceptedResponse.Accepted(registration.OutboxMessageId));
    }
}
