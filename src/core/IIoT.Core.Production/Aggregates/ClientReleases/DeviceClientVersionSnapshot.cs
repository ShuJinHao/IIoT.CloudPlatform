using IIoT.SharedKernel.Domain;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IIoT.Core.Production.Aggregates.ClientReleases;

public enum DeviceClientVersionReportUpdateResult
{
    Applied,
    Idempotent,
    Stale,
    Conflict
}

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
        string? remoteIpAddress = null,
        DateTime? receivedAtUtc = null)
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
            remoteIpAddress,
            receivedAtUtc);
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

    public DeviceClientVersionReportUpdateResult ReplaceReport(
        string clientCode,
        string hostVersion,
        string hostApiVersion,
        string channel,
        DateTime reportedAtUtc,
        IEnumerable<DeviceClientPluginVersion> installedPlugins,
        IEnumerable<string>? localIpAddresses = null,
        string? remoteIpAddress = null,
        DateTime? receivedAtUtc = null)
    {
        var normalizedClientCode =
            NormalizeRequired(clientCode, nameof(clientCode)).ToUpperInvariant();
        var normalizedHostVersion = NormalizeRequired(hostVersion, nameof(hostVersion));
        var normalizedHostApiVersion =
            NormalizeRequired(hostApiVersion, nameof(hostApiVersion));
        var normalizedChannel = NormalizeRequired(channel, nameof(channel));
        var normalizedReportedAtUtc = reportedAtUtc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(reportedAtUtc, DateTimeKind.Utc)
            : reportedAtUtc.ToUniversalTime();
        var normalizedLocalIpAddressesJson = SerializeIpAddresses(localIpAddresses);
        var normalizedRemoteIpAddress = NormalizeOptional(remoteIpAddress);
        var normalizedPlugins = installedPlugins
            .OrderBy(plugin => plugin.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(plugin => plugin.Version, StringComparer.Ordinal)
            .ToArray();

        if (ReportedAtUtc != default)
        {
            if (normalizedReportedAtUtc < ReportedAtUtc)
            {
                return DeviceClientVersionReportUpdateResult.Stale;
            }

            if (normalizedReportedAtUtc == ReportedAtUtc)
            {
                var incomingHash = ComputeContentSha256(
                    normalizedClientCode,
                    normalizedHostVersion,
                    normalizedHostApiVersion,
                    normalizedChannel,
                    normalizedPlugins,
                    normalizedLocalIpAddressesJson,
                    normalizedRemoteIpAddress);
                return string.Equals(
                    incomingHash,
                    GetContentSha256(),
                    StringComparison.Ordinal)
                    ? DeviceClientVersionReportUpdateResult.Idempotent
                    : DeviceClientVersionReportUpdateResult.Conflict;
            }
        }

        ClientCode = normalizedClientCode;
        HostVersion = normalizedHostVersion;
        HostApiVersion = normalizedHostApiVersion;
        Channel = normalizedChannel;
        ReportedAtUtc = normalizedReportedAtUtc;
        ReceivedAtUtc = NormalizeUtc(receivedAtUtc ?? DateTime.UtcNow);
        LocalIpAddressesJson = normalizedLocalIpAddressesJson;
        RemoteIpAddress = normalizedRemoteIpAddress;
        _installedPlugins.Clear();
        _installedPlugins.AddRange(normalizedPlugins);
        return DeviceClientVersionReportUpdateResult.Applied;
    }

    public string GetContentSha256()
        => ComputeContentSha256(
            ClientCode,
            HostVersion,
            HostApiVersion,
            Channel,
            _installedPlugins,
            LocalIpAddressesJson,
            RemoteIpAddress);

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
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToList();

        return JsonSerializer.Serialize(normalized);
    }

    private static string ComputeContentSha256(
        string clientCode,
        string hostVersion,
        string hostApiVersion,
        string channel,
        IEnumerable<DeviceClientPluginVersion> installedPlugins,
        string localIpAddressesJson,
        string? remoteIpAddress)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            clientCode,
            hostVersion,
            hostApiVersion,
            channel,
            localIpAddressesJson,
            remoteIpAddress,
            installedPlugins = installedPlugins
                .OrderBy(plugin => plugin.ModuleId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(plugin => plugin.Version, StringComparer.Ordinal)
                .Select(plugin => new
                {
                    plugin.ModuleId,
                    plugin.DisplayName,
                    plugin.Version,
                    plugin.HostApiVersion,
                    plugin.Enabled
                })
                .ToArray()
        });
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
}
