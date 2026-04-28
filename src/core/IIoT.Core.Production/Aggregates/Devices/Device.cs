using IIoT.Core.Production.Aggregates.Devices.Events;
using IIoT.Core.Production.Aggregates.Devices.ValueObjects;
using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.Devices;

/// <summary>
/// 云端管理的设备档案聚合根。
/// </summary>
public class Device : BaseEntity<Guid>
{
    protected Device()
    {
    }

    public Device(
        string deviceName,
        string code,
        Guid processId)
        : this(deviceName, DeviceCode.From(code), processId)
    {
    }

    public Device(
        string deviceName,
        DeviceCode code,
        Guid processId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        if (processId == Guid.Empty)
            throw new ArgumentException("ProcessId cannot be empty.", nameof(processId));

        Id = Guid.NewGuid();
        DeviceName = deviceName.Trim();
        Code = code.Value;
        ProcessId = processId;

        AddDomainEvent(new DeviceRegisteredDomainEvent(Id, DeviceName, Code, ProcessId));
    }

    public string DeviceName { get; private set; } = null!;

    public string Code { get; private set; } = null!;

    public Guid ProcessId { get; private set; }

    public uint RowVersion { get; private set; }

    public void Rename(string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        var normalizedName = newName.Trim();
        if (DeviceName == normalizedName)
        {
            return;
        }

        DeviceName = normalizedName;
        AddDomainEvent(new DeviceRenamedDomainEvent(Id, DeviceName));
    }

    public void ChangeProcess(Guid newProcessId)
    {
        if (newProcessId == Guid.Empty)
            throw new ArgumentException("ProcessId cannot be empty.", nameof(newProcessId));

        ProcessId = newProcessId;
    }

    public void MarkDeleted()
    {
        AddDomainEvent(new DeviceDeletedDomainEvent(Id, Code));
    }
}
