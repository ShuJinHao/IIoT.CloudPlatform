using IIoT.SharedKernel.Architecture;

namespace IIoT.Services.Contracts.RecordQueries;

public interface IDeviceOperationalStatusQueryService : IReadOnlyQueryPort
{
    Task<IReadOnlyList<DeviceOperationalStatusTarget>> GetScopedDevicesAsync(
        IReadOnlyCollection<Guid>? deviceIds = null,
        CancellationToken cancellationToken = default);
}

public sealed record DeviceOperationalStatusTarget(
    Guid DeviceId,
    string ClientCode);

public record DeviceStatusSummaryDto(
    int Total,
    int Online,
    int Warning,
    int Error,
    int Offline,
    DateTimeOffset GeneratedAt,
    string? SoftwareStatus = null,
    string? Issue = null);
