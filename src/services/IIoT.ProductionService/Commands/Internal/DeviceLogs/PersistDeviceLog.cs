using IIoT.Core.Production.Contracts.RecordRepositories;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.Contracts.Events.DeviceLogs;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.DeviceLogs;

/// <summary>
/// 设备日志落库命令。
/// </summary>
public record PersistDeviceLogCommand(
    DeviceLogReceivedEvent Event
) : ICommand<Result<bool>>;

public class PersistDeviceLogHandler(
    IDeviceIdentityQueryService deviceIdentityQuery,
    IDeviceLogRecordRepository repository
) : ICommandHandler<PersistDeviceLogCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        PersistDeviceLogCommand request,
        CancellationToken cancellationToken)
    {
        var evt = request.Event;

        if (evt.Logs.Count == 0)
            return Result.Success(true);

        var exists = await deviceIdentityQuery.ExistsAsync(
            evt.DeviceId, cancellationToken);

        if (!exists)
            return Result.Failure($"Persist failed: DeviceId {evt.DeviceId} does not exist.");

        var receivedAt = DateTime.UtcNow;

        var writeModels = evt.Logs.Select(item =>
        {
            var logTime = DeviceLogIdempotency.NormalizeLogTime(item.LogTime);

            return new DeviceLogWriteModel(
                Id: Guid.NewGuid(),
                DeviceId: evt.DeviceId,
                Level: item.Level,
                Message: item.Message,
                LogTime: logTime,
                ReceivedAt: receivedAt,
                IdempotencyKey: DeviceLogIdempotency.CreateKey(
                    evt.DeviceId,
                    item.Level,
                    item.Message,
                    logTime));
        }).ToList();

        await repository.InsertBatchAsync(writeModels, cancellationToken);

        return Result.Success(true);
    }
}
