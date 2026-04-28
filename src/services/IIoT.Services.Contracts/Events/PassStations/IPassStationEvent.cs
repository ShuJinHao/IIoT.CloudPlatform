namespace IIoT.Services.Contracts.Events.PassStations;

public interface IPassStationEvent
{
    int SchemaVersion { get; }

    Guid DeviceId { get; }
}
