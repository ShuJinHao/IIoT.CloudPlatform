namespace IIoT.Services.Common.Events.PassStations;

public interface IPassStationEvent
{
    Guid DeviceId { get; }
}
