using IIoT.Core.Production.Contracts.PassStation;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Events.PassStations;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.PassStations;

public sealed record PersistPassStationCommand(PassStationBatchReceivedEvent Event)
    : ICommand<Result<bool>>;

public sealed class PersistPassStationHandler(
    IDeviceIdentityQueryService deviceIdentityQuery,
    IPassStationRecordRepository repository)
    : ICommandHandler<PersistPassStationCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        PersistPassStationCommand request,
        CancellationToken cancellationToken)
    {
        var evt = request.Event;
        var exists = await deviceIdentityQuery.ExistsAsync(evt.DeviceId, cancellationToken);
        if (!exists)
            return Result.Failure($"过站数据落库失败:设备 {evt.DeviceId} 不存在。");

        if (evt.Items.Count == 0)
            return Result.Success(true);

        var receivedAt = DateTime.UtcNow;
        var records = evt.Items
            .Select(item => new PassStationRecordWriteModel(
                Guid.NewGuid(),
                evt.DeviceId,
                evt.TypeKey,
                item.Barcode,
                item.CellResult,
                item.CompletedTime,
                receivedAt,
                item.DeduplicationKey,
                item.PayloadJson))
            .ToArray();

        await repository.InsertBatchAsync(records, cancellationToken);
        return Result.Success(true);
    }
}
