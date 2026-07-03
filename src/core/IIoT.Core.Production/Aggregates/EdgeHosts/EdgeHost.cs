using IIoT.Core.Production.Aggregates.Devices.ValueObjects;
using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.EdgeHosts;

public sealed class EdgeHost : BaseEntity<Guid>, IAggregateRoot<Guid>
{
    public const int ClientCodeMaxLength = 50;
    public const int HostNameMaxLength = 128;
    public const int RemarkMaxLength = 512;

    private readonly List<EdgeHostPlcBinding> _plcBindings = [];

    private EdgeHost()
    {
    }

    public EdgeHost(
        Guid deviceId,
        string clientCode,
        string hostName,
        string? remark = null)
    {
        if (deviceId == Guid.Empty)
            throw new ArgumentException("DeviceId cannot be empty.", nameof(deviceId));

        Id = Guid.NewGuid();
        DeviceId = deviceId;
        ClientCode = NormalizeClientCode(clientCode);
        HostName = NormalizeRequired(hostName, nameof(hostName), HostNameMaxLength);
        Remark = NormalizeOptional(remark, RemarkMaxLength);
        Enabled = true;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid DeviceId { get; private set; }

    public string ClientCode { get; private set; } = null!;

    public string HostName { get; private set; } = null!;

    public bool Enabled { get; private set; }

    public string? Remark { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<EdgeHostPlcBinding> PlcBindings => _plcBindings.AsReadOnly();

    public void Rename(string hostName)
    {
        var normalized = NormalizeRequired(hostName, nameof(hostName), HostNameMaxLength);
        if (HostName == normalized)
        {
            return;
        }

        HostName = normalized;
        Touch();
    }

    public void UpdateRemark(string? remark)
    {
        var normalized = NormalizeOptional(remark, RemarkMaxLength);
        if (Remark == normalized)
        {
            return;
        }

        Remark = normalized;
        Touch();
    }

    public void Enable()
    {
        if (Enabled)
        {
            return;
        }

        Enabled = true;
        Touch();
    }

    public void Disable()
    {
        if (!Enabled)
        {
            return;
        }

        Enabled = false;
        Touch();
    }

    public EdgeHostPlcBinding AddPlcBinding(
        string plcCode,
        string plcName,
        Guid? processId = null,
        Guid? businessDeviceId = null,
        string? stationCode = null,
        string? protocol = null,
        string? address = null,
        int displayOrder = 0,
        string? remark = null,
        bool enabled = true)
    {
        var normalizedPlcCode = EdgeHostPlcBinding.NormalizePlcCode(plcCode);
        if (_plcBindings.Any(binding => binding.PlcCode == normalizedPlcCode))
        {
            throw new InvalidOperationException("PLC 编码已绑定到该上位机。");
        }

        var binding = new EdgeHostPlcBinding(
            Id,
            normalizedPlcCode,
            plcName,
            processId,
            businessDeviceId,
            stationCode,
            protocol,
            address,
            displayOrder,
            remark,
            enabled);
        _plcBindings.Add(binding);
        Touch();
        return binding;
    }

    public void UpdatePlcBinding(
        Guid bindingId,
        string plcName,
        Guid? processId = null,
        Guid? businessDeviceId = null,
        string? stationCode = null,
        string? protocol = null,
        string? address = null,
        int displayOrder = 0,
        string? remark = null)
    {
        var binding = FindRequiredPlcBinding(bindingId);
        binding.Update(plcName, processId, businessDeviceId, stationCode, protocol, address, displayOrder, remark);
        Touch();
    }

    public void EnablePlcBinding(Guid bindingId)
    {
        var binding = FindRequiredPlcBinding(bindingId);
        binding.Enable();
        Touch();
    }

    public void DisablePlcBinding(Guid bindingId)
    {
        var binding = FindRequiredPlcBinding(bindingId);
        binding.Disable();
        Touch();
    }

    public void RemovePlcBinding(Guid bindingId)
    {
        var binding = FindRequiredPlcBinding(bindingId);
        _plcBindings.Remove(binding);
        Touch();
    }

    public EdgeHostPlcBinding? FindPlcBinding(Guid bindingId)
    {
        return _plcBindings.FirstOrDefault(binding => binding.Id == bindingId);
    }

    public EdgeHostPlcBinding? FindPlcBinding(string plcCode)
    {
        var normalizedPlcCode = EdgeHostPlcBinding.NormalizePlcCode(plcCode);
        return _plcBindings.FirstOrDefault(binding => binding.PlcCode == normalizedPlcCode);
    }

    private EdgeHostPlcBinding FindRequiredPlcBinding(Guid bindingId)
    {
        if (bindingId == Guid.Empty)
            throw new ArgumentException("BindingId cannot be empty.", nameof(bindingId));

        return FindPlcBinding(bindingId)
            ?? throw new InvalidOperationException($"PLC binding '{bindingId}' does not exist on this edge host.");
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string NormalizeClientCode(string clientCode)
    {
        var normalized = DeviceCode.From(clientCode).Value;
        if (normalized.Length > ClientCodeMaxLength)
        {
            throw new ArgumentException($"ClientCode cannot exceed {ClientCodeMaxLength} characters.", nameof(clientCode));
        }

        return normalized;
    }

    internal static string NormalizeRequired(string value, string paramName, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"{paramName} cannot exceed {maxLength} characters.", paramName);
        }

        return normalized;
    }

    internal static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"Value cannot exceed {maxLength} characters.", nameof(value));
        }

        return normalized;
    }
}

public sealed class EdgeHostPlcBinding : BaseEntity<Guid>
{
    public const int PlcCodeMaxLength = 64;
    public const int PlcNameMaxLength = 128;
    public const int StationCodeMaxLength = 128;
    public const int ProtocolMaxLength = 64;
    public const int AddressMaxLength = 256;
    public const int RemarkMaxLength = 512;

    private EdgeHostPlcBinding()
    {
    }

    internal EdgeHostPlcBinding(
        Guid edgeHostId,
        string plcCode,
        string plcName,
        Guid? processId,
        Guid? businessDeviceId,
        string? stationCode,
        string? protocol,
        string? address,
        int displayOrder,
        string? remark,
        bool enabled)
    {
        if (edgeHostId == Guid.Empty)
            throw new ArgumentException("EdgeHostId cannot be empty.", nameof(edgeHostId));

        Id = Guid.NewGuid();
        EdgeHostId = edgeHostId;
        PlcCode = NormalizePlcCode(plcCode);
        ApplyDetails(plcName, processId, businessDeviceId, stationCode, protocol, address, displayOrder, remark);
        Enabled = enabled;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid EdgeHostId { get; private set; }

    public string PlcCode { get; private set; } = null!;

    public string PlcName { get; private set; } = null!;

    public Guid? ProcessId { get; private set; }

    public Guid? BusinessDeviceId { get; private set; }

    public string? StationCode { get; private set; }

    public string? Protocol { get; private set; }

    public string? Address { get; private set; }

    public bool Enabled { get; private set; }

    public int DisplayOrder { get; private set; }

    public string? Remark { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    internal void Update(
        string plcName,
        Guid? processId,
        Guid? businessDeviceId,
        string? stationCode,
        string? protocol,
        string? address,
        int displayOrder,
        string? remark)
    {
        ApplyDetails(plcName, processId, businessDeviceId, stationCode, protocol, address, displayOrder, remark);
        Touch();
    }

    internal void Enable()
    {
        if (Enabled)
        {
            return;
        }

        Enabled = true;
        Touch();
    }

    internal void Disable()
    {
        if (!Enabled)
        {
            return;
        }

        Enabled = false;
        Touch();
    }

    internal static string NormalizePlcCode(string plcCode)
    {
        return EdgeHost.NormalizeRequired(plcCode, nameof(plcCode), PlcCodeMaxLength)
            .ToUpperInvariant();
    }

    private void ApplyDetails(
        string plcName,
        Guid? processId,
        Guid? businessDeviceId,
        string? stationCode,
        string? protocol,
        string? address,
        int displayOrder,
        string? remark)
    {
        ProcessId = NormalizeOptionalId(processId, nameof(processId));
        BusinessDeviceId = NormalizeOptionalId(businessDeviceId, nameof(businessDeviceId));
        PlcName = EdgeHost.NormalizeRequired(plcName, nameof(plcName), PlcNameMaxLength);
        StationCode = EdgeHost.NormalizeOptional(stationCode, StationCodeMaxLength);
        Protocol = EdgeHost.NormalizeOptional(protocol, ProtocolMaxLength);
        Address = EdgeHost.NormalizeOptional(address, AddressMaxLength);
        DisplayOrder = displayOrder;
        Remark = EdgeHost.NormalizeOptional(remark, RemarkMaxLength);
    }

    private static Guid? NormalizeOptionalId(Guid? value, string paramName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
        }

        return value;
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
