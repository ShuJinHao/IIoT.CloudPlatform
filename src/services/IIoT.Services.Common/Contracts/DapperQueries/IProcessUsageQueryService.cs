namespace IIoT.Services.Common.Contracts.DapperQueries;

/// <summary>
/// 跨聚合的工序占用情况查询服务。
///
/// 用于 EmployeeService 在删除 MfgProcess 之前校验是否存在外部聚合引用,
/// 避免 EmployeeService 物理依赖 Production 域的聚合根。
///
/// 实现走 Dapper 直查 PG,不经过任何 EF Core 聚合根,
/// 因此不会破坏 DDD 分层 — Application 层只看到这个抽象契约。
/// </summary>
public interface IProcessUsageQueryService
{
    /// <summary>
    /// 判断指定工序下是否还存在 Device 引用。
    /// </summary>
    Task<bool> HasDeviceUnderProcessAsync(
        Guid processId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断指定工序下是否还存在 Recipe 引用。
    /// </summary>
    Task<bool> HasRecipeUnderProcessAsync(
        Guid processId,
        CancellationToken cancellationToken = default);
}