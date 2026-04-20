using IIoT.Core.Production.Contracts.PassStation;
using IIoT.Services.Contracts.Events.PassStations;

namespace IIoT.ProductionService.Commands.PassStations;

public sealed class StackingMapper
    : IPassStationMapper<PassDataStackingReceivedEvent, StackingWriteModel>
{
    public IReadOnlyCollection<StackingWriteModel> ToWriteModels(
        PassDataStackingReceivedEvent evt,
        DateTime receivedAt)
        =>
        [
            new StackingWriteModel(
                Id: Guid.NewGuid(),
                DeviceId: evt.DeviceId,
                Barcode: evt.Item.Barcode,
                TrayCode: evt.Item.TrayCode,
                SequenceNo: evt.Item.SequenceNo,
                LayerCount: evt.Item.LayerCount,
                CellResult: evt.Item.CellResult,
                CompletedTime: evt.Item.CompletedTime,
                ReceivedAt: receivedAt)
        ];
}
