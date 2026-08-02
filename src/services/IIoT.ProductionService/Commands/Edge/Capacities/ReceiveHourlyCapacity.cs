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
    int? OkCount = 0,
    int? NgCount = 0,
    string? PlcName = null,
    string? RequestId = null,
    int SchemaVersion = 1,
    string? ProcessType = null,
    string? PlcCode = null
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

        if (request.SchemaVersion is not 1 and not 2)
            return Result.Invalid($"产能数据 schemaVersion [{request.SchemaVersion}] 不受支持。");

        var device = await deviceIdentityQuery.GetByDeviceIdAsync(request.DeviceId, cancellationToken);
        if (device is null)
            return Result.Failure("数据接收失败: 设备不存在");

        var processType = Normalize(request.ProcessType);
        if (request.SchemaVersion == 2)
        {
            var registeredProcess = Normalize(device.ProcessCode);
            if (registeredProcess is null
                || processType is null
                || !string.Equals(registeredProcess, processType, StringComparison.Ordinal))
            {
                return Result.Forbidden("数据接收失败: 设备登记工序与产能上报工序不一致");
            }
        }

        var deduplicationKey = UploadDeduplicationKeys.ForHourlyCapacity(request);
        if (!deduplicationKey.IsSuccess)
            return Result.Failure(deduplicationKey.Errors?.ToArray() ?? []);

        var @event = mapper.Map<HourlyCapacityReceivedEvent>(request) with
        {
            SchemaVersion = request.SchemaVersion,
            ProcessType = request.SchemaVersion == 2 ? processType : null,
            PlcCode = request.SchemaVersion == 2
                ? request.PlcCode!.Trim()
                : request.PlcName?.Trim() ?? string.Empty,
            PlcName = request.PlcName?.Trim(),
            PlcNameIsTrusted = request.SchemaVersion == 2,
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

        var plcCode = @event.PlcCode;
        await cacheService.RemoveAsync(
            CacheKeys.CapacityHourly(request.DeviceId, request.Date, plcCode),
            cancellationToken);
        await cacheService.RemoveAsync(
            CacheKeys.CapacitySummary(request.DeviceId, request.Date, plcCode),
            cancellationToken);
        await cacheService.RemoveAsync(
            CacheKeys.CapacityRange(request.DeviceId, request.Date, request.Date, plcCode),
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

    private static string? Normalize(string? value)
    {
        value = value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value.ToLowerInvariant();
    }
}
