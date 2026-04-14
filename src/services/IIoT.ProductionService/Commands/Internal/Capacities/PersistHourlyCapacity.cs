using IIoT.Core.Production.Contracts.RecordRepositories;
using IIoT.Services.Common.Contracts.RecordQueries;
using IIoT.Services.Common.Events.Capacities;
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
    IHourlyCapacityRecordRepository repository
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
            ReportedAt: DateTime.UtcNow);

        await repository.UpsertAsync(writeModel, cancellationToken);

        return Result.Success(true);
    }
}
