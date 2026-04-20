using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.Contracts;
using IIoT.Core.Production.Contracts.RecordRepositories;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.Contracts.Events.Capacities;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Capacities;

/// <summary>
/// 半小时产能落库命令。
/// </summary>
public record PersistHourlyCapacityCommand(
    HourlyCapacityReceivedEvent Event
) : ICommand<Result<bool>>;

public class PersistHourlyCapacityHandler(
    IDeviceIdentityQueryService deviceIdentityQuery,
    IHourlyCapacityRecordRepository repository,
    ICacheService cacheService
) : ICommandHandler<PersistHourlyCapacityCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        PersistHourlyCapacityCommand request,
        CancellationToken cancellationToken)
    {
        var evt = request.Event;

        var exists = await deviceIdentityQuery.ExistsAsync(
            evt.DeviceId, cancellationToken);

        if (!exists)
            return Result.Failure($"Persist failed: DeviceId {evt.DeviceId} does not exist.");

        var reportedAt = evt.ReceivedAtUtc == default
            ? DateTime.UtcNow
            : DateTime.SpecifyKind(evt.ReceivedAtUtc, DateTimeKind.Utc);

        var writeModel = new HourlyCapacityWriteModel(
            Id: Guid.NewGuid(),
            DeviceId: evt.DeviceId,
            Date: evt.Date,
            ShiftCode: evt.ShiftCode,
            Hour: evt.Hour,
            Minute: evt.Minute,
            TimeLabel: evt.TimeLabel,
            TotalCount: evt.TotalCount,
            OkCount: evt.OkCount,
            NgCount: evt.NgCount,
            PlcName: evt.PlcName ?? string.Empty,
            ReportedAt: reportedAt);

        await repository.UpsertAsync(writeModel, cancellationToken);
        await cacheService.RemoveAsync(
            CacheKeys.CapacityHourly(evt.DeviceId, evt.Date, evt.PlcName),
            cancellationToken);
        await cacheService.RemoveAsync(
            CacheKeys.CapacitySummary(evt.DeviceId, evt.Date, evt.PlcName),
            cancellationToken);
        await cacheService.RemoveAsync(
            CacheKeys.CapacityRange(evt.DeviceId, evt.Date, evt.Date, evt.PlcName),
            cancellationToken);
        await cacheService.RemoveByPatternAsync(
            CacheKeys.CapacityHourlyPattern(evt.DeviceId),
            cancellationToken);
        await cacheService.RemoveByPatternAsync(
            CacheKeys.CapacitySummaryPattern(evt.DeviceId),
            cancellationToken);
        await cacheService.RemoveByPatternAsync(
            CacheKeys.CapacityRangePattern(evt.DeviceId),
            cancellationToken);
        await cacheService.RemoveByPatternAsync(
            CacheKeys.CapacityPagedByDevicePattern(evt.DeviceId),
            cancellationToken);

        return Result.Success(true);
    }
}
