using IIoT.SharedKernel.Paging;

namespace IIoT.Services.Common.Contracts.DapperQueries;

/// <summary>
/// 过站数据查询服务契约（Dapper 实现）
/// 云端后台管理端查询使用
/// </summary>
public interface IPassStationQueryService
{
    /// <summary>
    /// 通用条件查询注液工序过站数据（漏斗式，所有查询入口最终调这一个方法）
    /// </summary>
    Task<(List<dynamic> Items, int TotalCount)> GetInjectionByConditionAsync(
        Pagination pagination,
        List<Guid>? deviceIds = null,
        Guid? deviceId = null,
        string? barcode = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据 ID 获取单条注液过站数据详情（含全部字段）
    /// </summary>
    Task<dynamic?> GetInjectionDetailAsync(Guid id, CancellationToken cancellationToken = default);
}