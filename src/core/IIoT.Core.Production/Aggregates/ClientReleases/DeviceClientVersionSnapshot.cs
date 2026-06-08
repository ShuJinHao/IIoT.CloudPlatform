using IIoT.SharedKernel.Domain;

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
        IEnumerable<DeviceClientPluginVersion> installedPlugins)
    {
        if (deviceId == Guid.Empty)
        {
            throw new ArgumentException("DeviceId 不能为空。", nameof(deviceId));
        }

        Id = deviceId;
        DeviceId = deviceId;
        ReplaceReport(clientCode, hostVersion, hostApiVersion, channel, reportedAtUtc, installedPlugins);
    }

    public Guid DeviceId { get; private set; }

    public string ClientCode { get; private set; } = null!;

    public string HostVersion { get; private set; } = null!;

    public string HostApiVersion { get; private set; } = null!;

    public string Channel { get; private set; } = null!;

    public DateTime ReportedAtUtc { get; private set; }

    public DateTime ReceivedAtUtc { get; private set; }

    public IReadOnlyCollection<DeviceClientPluginVersion> InstalledPlugins => _installedPlugins.AsReadOnly();

    public void ReplaceReport(
        string clientCode,
        string hostVersion,
        string hostApiVersion,
        string channel,
        DateTime reportedAtUtc,
        IEnumerable<DeviceClientPluginVersion> installedPlugins)
    {
        ClientCode = NormalizeRequired(clientCode, nameof(clientCode)).ToUpperInvariant();
        HostVersion = NormalizeRequired(hostVersion, nameof(hostVersion));
        HostApiVersion = NormalizeRequired(hostApiVersion, nameof(hostApiVersion));
        Channel = NormalizeRequired(channel, nameof(channel));
        ReportedAtUtc = reportedAtUtc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(reportedAtUtc, DateTimeKind.Utc)
            : reportedAtUtc.ToUniversalTime();
        ReceivedAtUtc = DateTime.UtcNow;

        _installedPlugins.Clear();
        _installedPlugins.AddRange(installedPlugins);
    }

    private static string NormalizeRequired(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value.Trim();
    }
}
