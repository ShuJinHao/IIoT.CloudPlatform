using IIoT.Core.Production.ValueObjects;
using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.Devices;

/// <summary>
/// 聚合根:云端注册的上位机实例。
/// 唯一身份 = ClientInstanceId(MacAddress + ClientCode)。
/// 一台物理宿主机可承载多个上位机实例,因此 MacAddress 单独不构成身份。
/// </summary>
public class Device : IAggregateRoot
{
    /// <summary>
    /// 仅供 EF Core 物化使用,业务代码不要调用。
    /// </summary>
    protected Device() { }

    public Device(
        string deviceName,
        ClientInstanceId instance,
        Guid processId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        if (instance.IsEmpty)
            throw new ArgumentException("ClientInstanceId 不能为空。", nameof(instance));
        if (processId == Guid.Empty)
            throw new ArgumentException("ProcessId 不能为空。", nameof(processId));

        Id = Guid.NewGuid();
        DeviceName = deviceName.Trim();
        Instance = instance;
        ProcessId = processId;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public string DeviceName { get; private set; } = null!;

    /// <summary>
    /// 实例身份(物理宿主 + 上位机实例号)。
    /// EF 映射为 mac_address + client_code 两列,联合唯一索引。
    /// </summary>
    public ClientInstanceId Instance { get; private set; }

    public Guid ProcessId { get; private set; }

    public bool IsActive { get; private set; }

    // ── 行为 ──────────────────────────────────────────────

    public void Rename(string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        DeviceName = newName.Trim();
    }

    public void ChangeProcess(Guid newProcessId)
    {
        if (newProcessId == Guid.Empty)
            throw new ArgumentException("ProcessId 不能为空。", nameof(newProcessId));
        ProcessId = newProcessId;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}