using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.Devices;

/// <summary>
/// 聚合根：物理宿主机上的上位机实例
/// 唯一标识：MacAddress + ClientCode
/// </summary>
public class Device : IAggregateRoot
{
    protected Device()
    {
    }

    public Device(string deviceName, string macAddress, string clientCode, Guid processId)
    {
        Id = Guid.NewGuid();
        DeviceName = deviceName;
        MacAddress = macAddress;
        ClientCode = clientCode;
        ProcessId = processId;
        IsActive = true;
    }

    public Guid Id { get; set; }

    public string DeviceName { get; set; } = null!;

    /// <summary>
    /// 宿主机 MAC
    /// </summary>
    public string MacAddress { get; set; } = null!;

    /// <summary>
    /// 上位机实例标识
    /// </summary>
    public string ClientCode { get; set; } = null!;

    public Guid ProcessId { get; set; }

    public bool IsActive { get; set; }
}