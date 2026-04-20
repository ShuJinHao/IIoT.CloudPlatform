using IIoT.Core.Production.Contracts.PassStation;
using IIoT.Services.Contracts.Events.PassStations;

namespace IIoT.ProductionService.Commands.PassStations;

public sealed class InjectionMapper
    : IPassStationMapper<PassDataInjectionReceivedEvent, InjectionWriteModel>
{
    public IReadOnlyCollection<InjectionWriteModel> ToWriteModels(
        PassDataInjectionReceivedEvent evt,
        DateTime receivedAt)
        => evt.Items.Select(item => new InjectionWriteModel(
            Id: Guid.NewGuid(),
            DeviceId: evt.DeviceId,
            CellResult: item.CellResult,
            CompletedTime: item.CompletedTime,
            ReceivedAt: receivedAt,
            Barcode: item.Barcode,
            PreInjectionTime: item.PreInjectionTime,
            PreInjectionWeight: item.PreInjectionWeight,
            PostInjectionTime: item.PostInjectionTime,
            PostInjectionWeight: item.PostInjectionWeight,
            InjectionVolume: item.InjectionVolume
        )).ToList();
}
