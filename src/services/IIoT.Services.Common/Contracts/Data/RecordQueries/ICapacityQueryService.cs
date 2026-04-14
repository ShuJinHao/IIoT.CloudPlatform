using IIoT.SharedKernel.Paging;

namespace IIoT.Services.Common.Contracts.RecordQueries;

public interface ICapacityQueryService
{
    Task<List<HourlyCapacityDto>> GetHourlyByDeviceIdAsync(
        Guid deviceId,
        DateOnly date,
        string? plcName = null,
        CancellationToken cancellationToken = default);

    Task<DailySummaryDto?> GetSummaryByDeviceIdAsync(
        Guid deviceId,
        DateOnly date,
        string? plcName = null,
        CancellationToken cancellationToken = default);

    Task<List<DailyRangeSummaryDto>> GetSummaryRangeAsync(
        Guid deviceId,
        DateOnly startDate,
        DateOnly endDate,
        string? plcName = null,
        CancellationToken cancellationToken = default);

    Task<(List<DailyCapacityPagedItemDto> Items, int TotalCount)> GetDailyPagedAsync(
        Pagination pagination,
        DateOnly? date = null,
        Guid? deviceId = null,
        IReadOnlyCollection<Guid>? deviceIds = null,
        CancellationToken cancellationToken = default);
}

public record HourlyCapacityDto(
    int Hour,
    int Minute,
    string TimeLabel,
    string ShiftCode,
    int TotalCount,
    int OkCount,
    int NgCount);

public record DailySummaryDto(
    int TotalCount,
    int OkCount,
    int NgCount,
    int DayShiftTotal,
    int DayShiftOk,
    int DayShiftNg,
    int NightShiftTotal,
    int NightShiftOk,
    int NightShiftNg);

public record DailyRangeSummaryDto(
    DateOnly Date,
    int TotalCount,
    int OkCount,
    int NgCount,
    int DayShiftTotal,
    int DayShiftOk,
    int DayShiftNg,
    int NightShiftTotal,
    int NightShiftOk,
    int NightShiftNg);

public record DailyCapacityPagedItemDto(
    Guid DeviceId,
    string DeviceName,
    DateOnly Date,
    long TotalCount,
    long OkCount,
    long NgCount,
    decimal OkRate,
    DateTime ReportedAt);
