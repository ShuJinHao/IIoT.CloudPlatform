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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (processId == Guid.Empty)
            throw new ArgumentException("ProcessId cannot be empty.", nameof(processId));

        Id = Guid.NewGuid();
        DeviceName = deviceName.Trim();
        Code = NormalizeCode(code);
        ProcessId = processId;
    }

    public string DeviceName { get; private set; } = null!;

    public string Code { get; private set; } = null!;

    public Guid ProcessId { get; private set; }

    public void Rename(string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        DeviceName = newName.Trim();
    }

    public void ChangeProcess(Guid newProcessId)
    {
        if (newProcessId == Guid.Empty)
            throw new ArgumentException("ProcessId cannot be empty.", nameof(newProcessId));

        ProcessId = newProcessId;
    }

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
}
