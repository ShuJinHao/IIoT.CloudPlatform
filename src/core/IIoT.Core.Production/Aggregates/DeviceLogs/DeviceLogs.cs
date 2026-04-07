using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.DeviceLogs;

/// <summary>
/// 设备运行日志
/// </summary>
public class DeviceLog : IAggregateRoot
{
    protected DeviceLog()
    {
    }

    public DeviceLog(
        Guid deviceId,
        string macAddress,
        string clientCode,
        string level,
        string message,
        DateTime logTime)
    {
        Id = Guid.NewGuid();
        DeviceId = deviceId;
        MacAddress = macAddress;
        ClientCode = clientCode;
        Level = level;
        Message = message;
        LogTime = logTime;
        ReceivedAt = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    public Guid DeviceId { get; set; }

    public string MacAddress { get; set; } = null!;

    public string ClientCode { get; set; } = null!;

    public string Level { get; set; } = null!;

    public string Message { get; set; } = null!;

    public DateTime LogTime { get; set; }

    public DateTime ReceivedAt { get; set; }
}