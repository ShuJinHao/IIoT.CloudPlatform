using IIoT.SharedKernel.Paging;

namespace IIoT.Services.Contracts.RecordQueries;

public interface IDeviceLogQueryService
{
    Task<(List<DeviceLogListItemDto> Items, int TotalCount)> GetLogsByConditionAsync(
        Pagination pagination,
        Guid deviceId,
        string? level = null,
        string? keyword = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default);

    Task<List<DeviceLogListItemDto>> GetRecentLogsAsync(
        int limit,
        IReadOnlyCollection<string> normalizedLevels,
        Guid? processId = null,
        IReadOnlyCollection<Guid>? deviceIds = null,
        CancellationToken cancellationToken = default);

    Task<int> CountRecentAlertsAsync(
        DateTimeOffset windowStart,
        IReadOnlyCollection<string> normalizedLevels,
        Guid? processId = null,
        IReadOnlyCollection<Guid>? deviceIds = null,
        CancellationToken cancellationToken = default);
}

public record DeviceLogListItemDto(
    Guid Id,
    Guid DeviceId,
    string DeviceName,
    string Level,
    string Message,
    DateTime LogTime,
    DateTime ReceivedAt);

public record RecentAlertCountDto(
    int Count,
    int SinceHours,
    string MinLevel,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    DateTimeOffset GeneratedAt);

public static class DeviceLogSeverityLevels
{
    private static readonly string[] ErrorAndAbove = ["ERROR", "ERR"];
    public static readonly string[] WarningAndErrorLevels = ["WARN", "WARNING", "ERROR", "ERR"];
    private static readonly string[] InformationAndAbove = ["INFO", "INFORMATION", "WARN", "WARNING", "ERROR", "ERR"];

    public static bool TryGetLevelsAtOrAbove(
        string? minLevel,
        out IReadOnlyCollection<string> normalizedLevels,
        out string normalizedMinLevel)
    {
        var level = string.IsNullOrWhiteSpace(minLevel)
            ? "WARN"
            : minLevel.Trim().ToUpperInvariant();

        switch (level)
        {
            case "ERROR":
            case "ERR":
                normalizedLevels = ErrorAndAbove;
                normalizedMinLevel = "ERROR";
                return true;

            case "WARN":
            case "WARNING":
                normalizedLevels = WarningAndErrorLevels;
                normalizedMinLevel = "WARN";
                return true;

            case "INFO":
            case "INFORMATION":
                normalizedLevels = InformationAndAbove;
                normalizedMinLevel = "INFO";
                return true;

            default:
                normalizedLevels = [];
                normalizedMinLevel = string.Empty;
                return false;
        }
    }
}
