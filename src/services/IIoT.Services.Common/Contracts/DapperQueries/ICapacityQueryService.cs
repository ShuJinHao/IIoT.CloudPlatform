using IIoT.SharedKernel.Paging;

namespace IIoT.Services.Common.Contracts.DapperQueries;

/// <summary>
/// 产能汇总查询服务契约（Dapper 实现）
/// </summary>
public interface ICapacityQueryService
{
    /// <summary>
    /// 分页查询每日产能汇总（所有机台分页加载，JOIN 设备名称）
    /// </summary>
    Task<(List<dynamic> Items, int TotalCount)> GetDailyPagedAsync(
        Pagination pagination,
        DateOnly? date = null,
        Guid? deviceId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 单机台产能汇总（指定设备 + 日期范围，带设备名称）
    /// </summary>
    Task<List<dynamic>> GetDeviceSummaryAsync(
        Guid deviceId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken = default);
}