using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.PassStations;

public abstract class PassDataBase : IAggregateRoot
{
    protected PassDataBase()
    {
    }

    protected PassDataBase(
        Guid deviceId,
        string macAddress,
        string clientCode,
        string cellResult,
        DateTime completedTime)
    {
        Id = Guid.NewGuid();
        DeviceId = deviceId;
        MacAddress = macAddress;
        ClientCode = clientCode;
        CellResult = cellResult;
        CompletedTime = completedTime;
        ReceivedAt = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    public Guid DeviceId { get; set; }

    public string MacAddress { get; set; } = null!;

    public string ClientCode { get; set; } = null!;

    public string CellResult { get; set; } = null!;

    public DateTime CompletedTime { get; set; }

    public DateTime ReceivedAt { get; set; }
}