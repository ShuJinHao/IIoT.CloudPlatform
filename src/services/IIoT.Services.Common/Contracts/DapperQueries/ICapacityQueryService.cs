using IIoT.SharedKernel.Paging;

namespace IIoT.Services.Common.Contracts.DapperQueries;

/// <summary>
/// 产能查询服务契约
/// 统一使用 deviceId（Guid）作为唯一标识
/// </summary>
public interface ICapacityQueryService
{
    /// <summary>
    /// 按日查询半小时明细（日查询优先调用）
    /// </summary>
    Task<List<HourlyCapacityDto>> GetHourlyByDeviceIdAsync(
        Guid deviceId,
        DateOnly date,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按日查询汇总（日查询兜底）
    /// 白班+夜班合并为单对象，无数据返回 null
    /// </summary>
    Task<DailySummaryDto?> GetSummaryByDeviceIdAsync(
        Guid deviceId,
        DateOnly date,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按日期范围查询每日汇总（月/年查询使用，一次请求替代循环N次）
    /// 返回范围内每天有数据的汇总列表
    /// </summary>
    Task<List<DailyRangeSummaryDto>> GetSummaryRangeAsync(
        Guid deviceId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 云端后台分页查询（从 hourly_capacity 聚合为日粒度）
    /// </summary>
    Task<(List<dynamic> Items, int TotalCount)> GetDailyPagedAsync(
        Pagination pagination,
        DateOnly? date = null,
        Guid? deviceId = null,
        CancellationToken cancellationToken = default);
}

// ── DTO ──────────────────────────────────────────────────────────────

public record HourlyCapacityDto(
    int Hour,
    int Minute,
    string TimeLabel,
    string ShiftCode,
    int TotalCount,
    int OkCount,
    int NgCount
);

public record DailySummaryDto(
    int TotalCount,
    int OkCount,
    int NgCount,
    int DayShiftTotal,
    int DayShiftOk,
    int DayShiftNg,
    int NightShiftTotal,
    int NightShiftOk,
    int NightShiftNg
);

/// <summary>
/// 日期范围汇总 DTO（月/年查询每天一条）
/// </summary>
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
    int NightShiftNg
);