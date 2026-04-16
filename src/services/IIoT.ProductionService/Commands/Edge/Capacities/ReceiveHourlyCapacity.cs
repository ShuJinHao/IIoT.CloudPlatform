using AutoMapper;
using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.RecordQueries;
using IIoT.Services.Common.Events.Capacities;
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
    string? PlcName = null
) : IDeviceCommand<Result<bool>>;

public class ReceiveHourlyCapacityHandler(
    IDeviceIdentityQueryService deviceIdentityQuery,
    IMapper mapper,
    IEventPublisher eventPublisher,
    ICacheService cacheService
) : ICommandHandler<ReceiveHourlyCapacityCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        ReceiveHourlyCapacityCommand request,
        CancellationToken cancellationToken)
    {
        if (request.DeviceId == Guid.Empty)
            return Result.Failure("数据接收失败: DeviceId 不能为空");

        var exists = await deviceIdentityQuery.ExistsAsync(request.DeviceId, cancellationToken);
        if (!exists)
            return Result.Failure("数据接收失败: 设备不存在");

        var @event = mapper.Map<HourlyCapacityReceivedEvent>(request);
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
        await eventPublisher.PublishAsync(@event, cancellationToken);

        return Result.Success(true);
    }
}
