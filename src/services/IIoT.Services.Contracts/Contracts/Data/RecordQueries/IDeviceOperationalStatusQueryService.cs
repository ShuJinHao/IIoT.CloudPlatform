namespace IIoT.Services.Contracts.RecordQueries;

public interface IDeviceOperationalStatusQueryService
{
    Task<DeviceStatusSummaryDto> GetStatusSummaryAsync(
        DateTimeOffset offlineCutoff,
        DateTimeOffset statusWindowStart,
        IReadOnlyCollection<Guid>? deviceIds = null,
        CancellationToken cancellationToken = default);
}

public record DeviceStatusSummaryDto(
    int Total,
    int Online,
    int Warning,
    int Error,
    int Offline,
    DateTimeOffset GeneratedAt);
