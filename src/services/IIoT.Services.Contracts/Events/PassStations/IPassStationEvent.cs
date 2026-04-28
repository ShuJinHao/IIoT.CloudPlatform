using IIoT.Services.Contracts;

namespace IIoT.Services.Contracts.Events.PassStations;

public interface IPassStationEvent : IIntegrationEvent
{
    Guid DeviceId { get; }
}
