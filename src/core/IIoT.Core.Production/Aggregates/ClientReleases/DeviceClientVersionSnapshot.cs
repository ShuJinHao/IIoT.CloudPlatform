using IIoT.SharedKernel.Domain;
using System.Text.Json;

namespace IIoT.Core.Production.Aggregates.ClientReleases;

/// <summary>
/// 设备最近一次 Edge 客户端版本上报快照。
/// </summary>
public sealed class DeviceClientVersionSnapshot : BaseEntity<Guid>
{
    private readonly List<DeviceClientPluginVersion> _installedPlugins = [];

    private DeviceClientVersionSnapshot()
    {
    }

    public DeviceClientVersionSnapshot(
        Guid deviceId,
        string clientCode,
        string hostVersion,
        string hostApiVersion,
        string channel,
        DateTime reportedAtUtc,
        IEnumerable<DeviceClientPluginVersion> installedPlugins,
        IEnumerable<string>? localIpAddresses = null,
        string? remoteIpAddress = null)
    {
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("DeviceId 不能为空。", nameof(deviceId));
        }

        Id = deviceId;
        DeviceId = deviceId;
        ReplaceReport(
            clientCode,
            hostVersion,
            hostApiVersion,
            channel,
            reportedAtUtc,
            installedPlugins,
            localIpAddresses,
            remoteIpAddress);
    }

    public Guid DeviceId { get; private set; }

    public string ClientCode { get; private set; } = null!;

    public string HostVersion { get; private set; } = null!;

    public string HostApiVersion { get; private set; } = null!;

    public string Channel { get; private set; } = null!;

    public DateTime ReportedAtUtc { get; private set; }

    public DateTime ReceivedAtUtc { get; private set; }

    public string LocalIpAddressesJson { get; private set; } = "[]";

    public string? RemoteIpAddress { get; private set; }

    public IReadOnlyCollection<DeviceClientPluginVersion> InstalledPlugins => _installedPlugins.AsReadOnly();

    public void ReplaceReport(
        string clientCode,
        string hostVersion,
        string hostApiVersion,
        string channel,
        DateTime reportedAtUtc,
        IEnumerable<DeviceClientPluginVersion> installedPlugins,
        IEnumerable<string>? localIpAddresses = null,
        string? remoteIpAddress = null)
    {
        ClientCode = NormalizeRequired(clientCode, nameof(clientCode)).ToUpperInvariant();
        HostVersion = NormalizeRequired(hostVersion, nameof(hostVersion));
        HostApiVersion = NormalizeRequired(hostApiVersion, nameof(hostApiVersion));
        Channel = NormalizeRequired(channel, nameof(channel));
        ReportedAtUtc = reportedAtUtc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(reportedAtUtc, DateTimeKind.Utc)
            : reportedAtUtc.ToUniversalTime();
        ReceivedAtUtc = DateTime.UtcNow;
        LocalIpAddressesJson = SerializeIpAddresses(localIpAddresses);
        RemoteIpAddress = NormalizeOptional(remoteIpAddress);

        _installedPlugins.Clear();
        _installedPlugins.AddRange(installedPlugins);
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
}
