using IIoT.Core.Production.Contracts.PassStation;

namespace IIoT.ProductionService.Commands.PassStations;

public interface IPassStationMapper<in TEvent, TWriteModel>
    where TEvent : class
    where TWriteModel : IPassStationWriteModel
{
    IReadOnlyCollection<TWriteModel> ToWriteModels(TEvent evt, DateTime receivedAt);
}
