using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.ClientReleases;

/// <summary>
/// Authoritative one-to-one business binding between a Cloud device and one
/// independently published plugin series. Installation state never changes
/// this relationship.
/// </summary>
public sealed class DevicePluginBinding : BaseEntity<Guid>, IAggregateRoot<Guid>
{
    private DevicePluginBinding()
    {
    }

    public DevicePluginBinding(
        Guid deviceId,
        Guid clientReleaseComponentId,
        string processType,
        DateTime boundAtUtc,
        Guid? id = null)
    {
        if (deviceId == Guid.Empty)
            throw new ArgumentException("DeviceId 不能为空。", nameof(deviceId));
        if (clientReleaseComponentId == Guid.Empty)
            throw new ArgumentException("插件发布系列不能为空。", nameof(clientReleaseComponentId));

        Id = id ?? Guid.NewGuid();
        DeviceId = deviceId;
        ClientReleaseComponentId = clientReleaseComponentId;
        ProcessType = BusinessIdentityNormalization.NormalizeClassificationCode(
            processType,
            nameof(processType));
        BoundAtUtc = NormalizeUtc(boundAtUtc);
    }

    public Guid DeviceId { get; private set; }

    public Guid ClientReleaseComponentId { get; private set; }

    public string ProcessType { get; private set; } = null!;

    public DateTime BoundAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
}
