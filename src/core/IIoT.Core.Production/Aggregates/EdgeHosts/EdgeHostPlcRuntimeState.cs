using IIoT.Core.Production.Aggregates.Devices.ValueObjects;
using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.EdgeHosts;

/// <summary>
/// PLC runtime status projection reported by an edge client.
/// This is a state projection, not an aggregate root and not a Cloud-side configuration source.
/// </summary>
public sealed class EdgeHostPlcRuntimeState : BaseEntity<Guid>
{
    public const int ClientCodeMaxLength = 50;
    public const int PlcCodeMaxLength = 64;
    public const int PlcNameMaxLength = 128;
    public const int RuntimeStatusMaxLength = 32;
    public const int StationCodeMaxLength = 128;
    public const int ProtocolMaxLength = 64;
    public const int AddressMaxLength = 256;
    public const int LastErrorMaxLength = 1024;

    private EdgeHostPlcRuntimeState()
    {
    }

    public EdgeHostPlcRuntimeState(
        Guid deviceId,
        string clientCode,
        string plcCode)
    {
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("DeviceId 不能为空。", nameof(deviceId));
        }

        Id = Guid.NewGuid();
        DeviceId = deviceId;
        ClientCode = NormalizeClientCode(clientCode);
        PlcCode = NormalizePlcCode(plcCode);
        RuntimeStatus = EdgeHostPlcRuntimeStatus.Unknown;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid DeviceId { get; private set; }

    public string ClientCode { get; private set; } = null!;

    public string PlcCode { get; private set; } = null!;

    public string? ReportedPlcName { get; private set; }

    public bool IsConnected { get; private set; }

    public string RuntimeStatus { get; private set; } = null!;

    public string? StationCode { get; private set; }

    public string? Protocol { get; private set; }

    public string? Address { get; private set; }

    public string? LastError { get; private set; }

    public DateTime LastSeenAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public void ReplaceReport(
        string? reportedPlcName,
        bool isConnected,
        string? runtimeStatus,
        DateTime observedAtUtc,
        string? stationCode = null,
        string? protocol = null,
        string? address = null,
        string? lastError = null)
    {
        var normalizedLastError = NormalizeOptional(lastError, LastErrorMaxLength);

        ReportedPlcName = NormalizeOptional(reportedPlcName, PlcNameMaxLength);
        IsConnected = isConnected;
        RuntimeStatus = NormalizeRuntimeStatus(runtimeStatus, isConnected, normalizedLastError);
        LastSeenAtUtc = NormalizeUtc(observedAtUtc);
        StationCode = NormalizeOptional(stationCode, StationCodeMaxLength);
        Protocol = NormalizeOptional(protocol, ProtocolMaxLength);
        Address = NormalizeOptional(address, AddressMaxLength);
        LastError = normalizedLastError;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public static string NormalizeClientCode(string clientCode)
    {
        var normalized = DeviceCode.From(clientCode).Value;
        if (normalized.Length > ClientCodeMaxLength)
        {
            throw new ArgumentException($"ClientCode 不能超过 {ClientCodeMaxLength} 个字符。", nameof(clientCode));
        }

        return normalized;
    }

    public static string NormalizePlcCode(string plcCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plcCode, nameof(plcCode));
        var normalized = plcCode.Trim().ToUpperInvariant();
        if (normalized.Length > PlcCodeMaxLength)
        {
            throw new ArgumentException($"PLC 编码不能超过 {PlcCodeMaxLength} 个字符。", nameof(plcCode));
        }

        return normalized;
    }

    private static string NormalizeRuntimeStatus(string? runtimeStatus, bool isConnected, string? lastError)
    {
        if (string.IsNullOrWhiteSpace(runtimeStatus))
        {
            if (!isConnected && !string.IsNullOrWhiteSpace(lastError))
            {
                return EdgeHostPlcRuntimeStatus.Faulted;
            }

            return isConnected
                ? EdgeHostPlcRuntimeStatus.Connected
                : EdgeHostPlcRuntimeStatus.Disconnected;
        }

        return runtimeStatus.Trim().ToUpperInvariant() switch
        {
            "CONNECTED" => EdgeHostPlcRuntimeStatus.Connected,
            "DISCONNECTED" => EdgeHostPlcRuntimeStatus.Disconnected,
            "FAULTED" => EdgeHostPlcRuntimeStatus.Faulted,
            "UNKNOWN" => EdgeHostPlcRuntimeStatus.Unknown,
            _ => throw new ArgumentOutOfRangeException(
                nameof(runtimeStatus),
                "PLC runtime status 必须是 Connected、Disconnected、Faulted 或 Unknown。")
        };
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"Value 不能超过 {maxLength} 个字符。", nameof(value));
        }

        return normalized;
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
}

public static class EdgeHostPlcRuntimeStatus
{
    public const string Connected = "Connected";
    public const string Disconnected = "Disconnected";
    public const string Faulted = "Faulted";
    public const string Unknown = "Unknown";
}
