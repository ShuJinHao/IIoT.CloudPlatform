using IIoT.SharedKernel.Domain;
using System.Text.Json;

namespace IIoT.Core.Production.Aggregates.ClientReleases;

public enum RuntimeHeartbeatReportUpdateResult
{
    Applied,
    Idempotent,
    Stale,
    Conflict
}

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
        string? remoteIpAddress = null,
        DateTime? receivedAtUtc = null)
    {
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("DeviceId 不能为空。", nameof(deviceId));
        }

        Id = Guid.NewGuid();
        DeviceId = deviceId;
        ClientCode = NormalizeRequired(clientCode, nameof(clientCode)).ToUpperInvariant();
        var normalizedReceivedAtUtc = NormalizeUtc(receivedAtUtc ?? DateTime.UtcNow);
        CreatedAtUtc = normalizedReceivedAtUtc;
        ReplaceReport(
            runtimeInstanceId,
            machineProfile,
            hostVersion,
            hostApiVersion,
            status,
            startedAtUtc,
            reportedAtUtc,
            localIpAddresses,
            remoteIpAddress,
            normalizedReceivedAtUtc);
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

    public RuntimeHeartbeatReportUpdateResult ReplaceReport(
        string runtimeInstanceId,
        string? machineProfile,
        string hostVersion,
        string hostApiVersion,
        string status,
        DateTime startedAtUtc,
        DateTime reportedAtUtc,
        IEnumerable<string>? localIpAddresses = null,
        string? remoteIpAddress = null,
        DateTime? receivedAtUtc = null)
    {
        var normalizedRuntimeInstanceId = NormalizeRequired(runtimeInstanceId, nameof(runtimeInstanceId));
        var normalizedMachineProfile = NormalizeOptional(machineProfile);
        var normalizedHostVersion = NormalizeRequired(hostVersion, nameof(hostVersion));
        var normalizedHostApiVersion = NormalizeRequired(hostApiVersion, nameof(hostApiVersion));
        var normalizedStatus = NormalizeStatus(status);
        var normalizedStartedAtUtc = NormalizeUtc(startedAtUtc);
        var normalizedReportedAtUtc = NormalizeUtc(reportedAtUtc);
        var normalizedLocalIpAddressesJson = SerializeIpAddresses(localIpAddresses);
        var normalizedRemoteIpAddress = NormalizeOptional(remoteIpAddress);

        if (LastHeartbeatAtUtc != default)
        {
            if (normalizedReportedAtUtc < LastHeartbeatAtUtc)
            {
                return RuntimeHeartbeatReportUpdateResult.Stale;
            }

            if (normalizedReportedAtUtc == LastHeartbeatAtUtc)
            {
                return MatchesCurrentReport(
                    normalizedRuntimeInstanceId,
                    normalizedMachineProfile,
                    normalizedHostVersion,
                    normalizedHostApiVersion,
                    normalizedStatus,
                    normalizedStartedAtUtc,
                    normalizedLocalIpAddressesJson,
                    normalizedRemoteIpAddress)
                    ? RuntimeHeartbeatReportUpdateResult.Idempotent
                    : RuntimeHeartbeatReportUpdateResult.Conflict;
            }
        }

        RuntimeInstanceId = normalizedRuntimeInstanceId;
        MachineProfile = normalizedMachineProfile;
        HostVersion = normalizedHostVersion;
        HostApiVersion = normalizedHostApiVersion;
        Status = normalizedStatus;
        StartedAtUtc = normalizedStartedAtUtc;
        LastHeartbeatAtUtc = normalizedReportedAtUtc;
        LastStoppedAtUtc = Status is "Stopping" or "Stopped"
            ? LastHeartbeatAtUtc
            : LastStoppedAtUtc;
        UpdatedAtUtc = NormalizeUtc(receivedAtUtc ?? DateTime.UtcNow);
        LocalIpAddressesJson = normalizedLocalIpAddressesJson;
        RemoteIpAddress = normalizedRemoteIpAddress;
        return RuntimeHeartbeatReportUpdateResult.Applied;
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

    private bool MatchesCurrentReport(
        string runtimeInstanceId,
        string? machineProfile,
        string hostVersion,
        string hostApiVersion,
        string status,
        DateTime startedAtUtc,
        string localIpAddressesJson,
        string? remoteIpAddress)
    {
        return string.Equals(RuntimeInstanceId, runtimeInstanceId, StringComparison.Ordinal)
               && string.Equals(MachineProfile, machineProfile, StringComparison.Ordinal)
               && string.Equals(HostVersion, hostVersion, StringComparison.Ordinal)
               && string.Equals(HostApiVersion, hostApiVersion, StringComparison.Ordinal)
               && string.Equals(Status, status, StringComparison.Ordinal)
               && StartedAtUtc == startedAtUtc
               && string.Equals(LocalIpAddressesJson, localIpAddressesJson, StringComparison.Ordinal)
               && string.Equals(RemoteIpAddress, remoteIpAddress, StringComparison.Ordinal);
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
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToList();

        return JsonSerializer.Serialize(normalized);
    }
}
