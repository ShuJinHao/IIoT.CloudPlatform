using IIoT.SharedKernel.Domain;
using System.Text.Json;

namespace IIoT.Core.Production.Aggregates.ClientReleases;

/// <summary>
/// 设备客户端状态投影。版本安装事实来自版本快照，软件运行事实来自运行心跳。
/// </summary>
public sealed class DeviceClientState : BaseEntity<Guid>
{
    private DeviceClientState()
    {
    }

    public DeviceClientState(Guid deviceId, string clientCode)
    {
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("DeviceId 不能为空。", nameof(deviceId));
        }

        Id = Guid.NewGuid();
        DeviceId = deviceId;
        ClientCode = NormalizeRequired(clientCode, nameof(clientCode)).ToUpperInvariant();
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid DeviceId { get; private set; }

    public string ClientCode { get; private set; } = null!;

    public string? Channel { get; private set; }

    public string? HostVersion { get; private set; }

    public string? HostApiVersion { get; private set; }

    public string VersionLocalIpAddressesJson { get; private set; } = "[]";

    public string? VersionRemoteIpAddress { get; private set; }

    public DateTime? VersionReportedAtUtc { get; private set; }

    public DateTime? VersionReceivedAtUtc { get; private set; }

    public string? RuntimeInstanceId { get; private set; }

    public string? MachineProfile { get; private set; }

    public string? RuntimeHostVersion { get; private set; }

    public string? RuntimeHostApiVersion { get; private set; }

    public string? RuntimeStatus { get; private set; }

    public string RuntimeLocalIpAddressesJson { get; private set; } = "[]";

    public string? RuntimeRemoteIpAddress { get; private set; }

    public DateTime? RuntimeStartedAtUtc { get; private set; }

    public DateTime? LastRuntimeHeartbeatAtUtc { get; private set; }

    public DateTime? LastRuntimeStoppedAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public void ApplyVersionReport(DeviceClientVersionSnapshot snapshot)
    {
        if (snapshot.DeviceId != DeviceId
            || !string.Equals(snapshot.ClientCode, ClientCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("设备客户端状态与版本快照身份不一致。");
        }

        Channel = NormalizeOptional(snapshot.Channel);
        HostVersion = NormalizeOptional(snapshot.HostVersion);
        HostApiVersion = NormalizeOptional(snapshot.HostApiVersion);
        VersionLocalIpAddressesJson = SerializeIpAddresses(snapshot.GetLocalIpAddresses());
        VersionRemoteIpAddress = NormalizeOptional(snapshot.RemoteIpAddress);
        VersionReportedAtUtc = NormalizeUtc(snapshot.ReportedAtUtc);
        VersionReceivedAtUtc = NormalizeUtc(snapshot.ReceivedAtUtc);
        Touch();
    }

    public void ApplyRuntimeHeartbeat(EdgeDeviceRuntimeHeartbeat heartbeat)
    {
        if (heartbeat.DeviceId != DeviceId
            || !string.Equals(heartbeat.ClientCode, ClientCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("设备客户端状态与运行心跳身份不一致。");
        }

        RuntimeInstanceId = NormalizeOptional(heartbeat.RuntimeInstanceId);
        MachineProfile = NormalizeOptional(heartbeat.MachineProfile);
        RuntimeHostVersion = NormalizeOptional(heartbeat.HostVersion);
        RuntimeHostApiVersion = NormalizeOptional(heartbeat.HostApiVersion);
        RuntimeStatus = NormalizeOptional(heartbeat.Status);
        RuntimeLocalIpAddressesJson = SerializeIpAddresses(heartbeat.GetLocalIpAddresses());
        RuntimeRemoteIpAddress = NormalizeOptional(heartbeat.RemoteIpAddress);
        RuntimeStartedAtUtc = NormalizeUtc(heartbeat.StartedAtUtc);
        LastRuntimeHeartbeatAtUtc = NormalizeUtc(heartbeat.LastHeartbeatAtUtc);
        LastRuntimeStoppedAtUtc = heartbeat.LastStoppedAtUtc.HasValue
            ? NormalizeUtc(heartbeat.LastStoppedAtUtc.Value)
            : null;
        Touch();
    }

    public IReadOnlyList<string> GetVersionLocalIpAddresses()
        => DeserializeIpAddresses(VersionLocalIpAddressesJson);

    public IReadOnlyList<string> GetRuntimeLocalIpAddresses()
        => DeserializeIpAddresses(RuntimeLocalIpAddressesJson);

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
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

    private static IReadOnlyList<string> DeserializeIpAddresses(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
