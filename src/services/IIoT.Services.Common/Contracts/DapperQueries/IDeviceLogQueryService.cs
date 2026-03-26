using IIoT.SharedKernel.Paging;

namespace IIoT.Services.Common.Contracts.DapperQueries;

/// <summary>
/// 设备日志查询服务契约（Dapper 实现）
/// </summary>
public interface IDeviceLogQueryService
{
    /// <summary>
    /// 通用条件查询设备日志（漏斗式，所有查询入口最终调这一个方法）
    /// </summary>
    Task<(List<dynamic> Items, int TotalCount)> GetLogsByConditionAsync(
        Pagination pagination,
        Guid deviceId,
        string? level = null,
        string? keyword = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default);
}