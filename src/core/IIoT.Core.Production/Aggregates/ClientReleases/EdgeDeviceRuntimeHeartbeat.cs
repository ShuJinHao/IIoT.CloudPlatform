using IIoT.SharedKernel.Domain;
using System.Text.Json;

namespace IIoT.Core.Production.Aggregates.ClientReleases;

/// <summary>
/// Edge 客户端运行态心跳快照，只表示 Shell/宿主进程运行状态，不表示版本安装状态或上传门控。
/// </summary>
public sealed class EdgeDeviceRuntimeHeartbeat : BaseEntity<Guid>
{
    private EdgeDeviceRuntimeHeartbeat()
    {
    }

    public EdgeDeviceRuntimeHeartbeat(
        Guid deviceId,
        string clientCode,
        string runtimeInstanceId,
        string? machineProfile,
        string hostVersion,
        string hostApiVersion,
        string status,
        DateTime startedAtUtc,
        DateTime reportedAtUtc,
        IEnumerable<string>? localIpAddresses = null,
        string? remoteIpAddress = null)
    {
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("DeviceId 不能为空。", nameof(deviceId));
        }

        Id = Guid.NewGuid();
        DeviceId = deviceId;
        ClientCode = NormalizeRequired(clientCode, nameof(clientCode)).ToUpperInvariant();
        CreatedAtUtc = DateTime.UtcNow;
        ReplaceReport(
            runtimeInstanceId,
            machineProfile,
            hostVersion,
            hostApiVersion,
            status,
            startedAtUtc,
            reportedAtUtc,
            localIpAddresses,
            remoteIpAddress);
    }

    public Guid DeviceId { get; private set; }

    public string ClientCode { get; private set; } = null!;

    public string RuntimeInstanceId { get; private set; } = null!;

    public string? MachineProfile { get; private set; }

    public string HostVersion { get; private set; } = null!;

    public string HostApiVersion { get; private set; } = null!;

    public string Status { get; private set; } = null!;

    public string LocalIpAddressesJson { get; private set; } = "[]";

    public string? RemoteIpAddress { get; private set; }

    public DateTime StartedAtUtc { get; private set; }

    public DateTime LastHeartbeatAtUtc { get; private set; }

    public DateTime? LastStoppedAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public void ReplaceReport(
        string runtimeInstanceId,
        string? machineProfile,
        string hostVersion,
        string hostApiVersion,
        string status,
        DateTime startedAtUtc,
        DateTime reportedAtUtc,
        IEnumerable<string>? localIpAddresses = null,
        string? remoteIpAddress = null)
    {
        RuntimeInstanceId = NormalizeRequired(runtimeInstanceId, nameof(runtimeInstanceId));
        MachineProfile = NormalizeOptional(machineProfile);
        HostVersion = NormalizeRequired(hostVersion, nameof(hostVersion));
        HostApiVersion = NormalizeRequired(hostApiVersion, nameof(hostApiVersion));
        Status = NormalizeStatus(status);
        StartedAtUtc = NormalizeUtc(startedAtUtc);
        LastHeartbeatAtUtc = NormalizeUtc(reportedAtUtc);
        LastStoppedAtUtc = Status is "Stopping" or "Stopped"
            ? LastHeartbeatAtUtc
            : LastStoppedAtUtc;
        UpdatedAtUtc = DateTime.UtcNow;
        LocalIpAddressesJson = SerializeIpAddresses(localIpAddresses);
        RemoteIpAddress = NormalizeOptional(remoteIpAddress);
    }

    public IReadOnlyList<string> GetLocalIpAddresses()
    {
        if (string.IsNullOrWhiteSpace(LocalIpAddressesJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(LocalIpAddressesJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

    private static string NormalizeRequired(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string NormalizeStatus(string value)
    {
        var normalized = NormalizeRequired(value, nameof(value));
        return normalized.ToUpperInvariant() switch
        {
            "STARTING" => "Starting",
            "RUNNING" => "Running",
            "STOPPING" => "Stopping",
            "STOPPED" => "Stopped",
            _ => throw new ArgumentOutOfRangeException(nameof(value), "运行状态必须是 Starting、Running、Stopping 或 Stopped。")
        };
    }

    private static string SerializeIpAddresses(IEnumerable<string>? values)
    {
        var normalized = (values ?? [])
            .Select(NormalizeOptional)
            .Where(value => value is not null)
            .Select(value => value!)
            .Where(value => value.Length <= 128)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToList();

        return JsonSerializer.Serialize(normalized);
    }
}
