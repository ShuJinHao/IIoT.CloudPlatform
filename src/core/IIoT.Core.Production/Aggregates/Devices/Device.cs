using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.Devices;

/// <summary>
/// 聚合根：物理设备/上位机终端
/// </summary>
public class Device : IAggregateRoot
{
    protected Device()
    {
    }

    public Device(string deviceCode, string macAddress, Guid processId)
    {
        Id = Guid.NewGuid();
        DeviceCode = deviceCode;
        MacAddress = macAddress;
        ProcessId = processId;
        IsActive = true;
    }

    /// <summary>
    /// 设备全局唯一标识 (UUID)
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 设备编号 (如: Stacker-01)
    /// </summary>
    public string DeviceCode { get; set; } = null!;

    /// <summary>
    /// 🌟 核心防伪标识：设备的物理 MAC 地址，用于开机向云端自证身份
    /// </summary>
    public string MacAddress { get; set; } = null!;

    /// <summary>
    /// 归属：这台机器属于哪个工序？
    /// ⚠️ 注意：这里只有 UUID，没有 MfgProcess 实体引用，实现了跨类库完美解耦！
    /// </summary>
    public Guid ProcessId { get; set; }

    /// <summary>
    /// 设备状态 (true: 正常运行; false: 设备报废或停用)
    /// </summary>
    public bool IsActive { get; set; }
}