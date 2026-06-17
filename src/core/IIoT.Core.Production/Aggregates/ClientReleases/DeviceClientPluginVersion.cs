using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.ClientReleases;

/// <summary>
/// 设备最近一次版本上报中的插件明细。
/// </summary>
public sealed class DeviceClientPluginVersion : IEntity<Guid>
{
    private DeviceClientPluginVersion()
    {
    }

    public DeviceClientPluginVersion(
        string moduleId,
        string? displayName,
        string version,
        string? hostApiVersion,
        bool enabled)
    {
        Id = Guid.NewGuid();
        ModuleId = NormalizeRequired(moduleId, nameof(moduleId));
        DisplayName = NormalizeOptional(displayName);
        Version = NormalizeRequired(version, nameof(version));
        HostApiVersion = NormalizeOptional(hostApiVersion);
        Enabled = enabled;
    }

    public Guid Id { get; private set; }

    public Guid DeviceClientVersionSnapshotId { get; private set; }

    public string ModuleId { get; private set; } = null!;

    public string? DisplayName { get; private set; }

    public string Version { get; private set; } = null!;

    public string? HostApiVersion { get; private set; }

    public bool Enabled { get; private set; }

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
}
