namespace IIoT.Services.Common.Contracts;

/// <summary>
/// 跨聚合的轻量只读查询入口。
///
/// 用于 Application 层 Handler 在执行业务规则时做跨聚合的存在性/计数校验
/// (例如"工序是否存在"、"设备是否在某工序下"、"配方版本号是否被占用"),
/// 避免为每一种校验都建一个 Specification 类、注入多个 Repository、
/// 或为每个跨域校验单独建一个 IXxxQueryService。
///
/// 设计约束:
/// - 所有方法只返回 bool / int / 简单标量,绝不返回实体或 IQueryable
/// - 实现走 EF Core 的 DbContext,翻译成 SQL EXISTS / COUNT
/// - 由 IIoT.EntityFrameworkCore 项目提供唯一实现
/// </summary>
public interface IDataQueryService
{
    // ── MfgProcess(工序)──────────────────────────────────

    /// <summary>
    /// 判断指定 ID 的工序是否存在。
    /// </summary>
    Task<bool> MfgProcessExistsAsync(
        Guid processId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断工序编码是否已被占用。
    /// excludeProcessId 用于"修改时排除自身"场景。
    /// </summary>
    Task<bool> MfgProcessCodeOccupiedAsync(
        string processCode,
        Guid? excludeProcessId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断指定工序下是否还存在 Device 引用。
    /// 删除工序前的安全闸门。
    /// </summary>
    Task<bool> HasDeviceUnderProcessAsync(
        Guid processId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断指定工序下是否还存在 Recipe 引用。
    /// 删除工序前的安全闸门。
    /// </summary>
    Task<bool> HasRecipeUnderProcessAsync(
        Guid processId,
        CancellationToken cancellationToken = default);

    // ── Device(设备)─────────────────────────────────────

    /// <summary>
    /// 判断指定 ID 的设备是否存在(不论是否活跃)。
    /// </summary>
    Task<bool> DeviceExistsAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断指定 ID 的设备是否存在且处于活跃状态。
    /// 边缘端上行数据接收时使用。
    /// </summary>
    Task<bool> ActiveDeviceExistsAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断指定设备是否存在且属于指定工序。
    /// 配方创建时校验"机台属于当前工序"使用。
    /// </summary>
    Task<bool> DeviceUnderProcessExistsAsync(
        Guid deviceId,
        Guid processId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断指定 ClientInstanceId(MAC + ClientCode 联合身份)是否已被占用。
    /// 设备注册时校验联合唯一性使用。
    /// </summary>
    Task<bool> DeviceInstanceOccupiedAsync(
        string macAddress,
        string clientCode,
        CancellationToken cancellationToken = default);

    // ── Recipe(配方)─────────────────────────────────────

    /// <summary>
    /// 判断同名同工序同设备下指定版本的配方是否已存在。
    /// 配方创建/升级时的版本号防重校验使用。
    /// </summary>
    Task<bool> RecipeVersionOccupiedAsync(
        string recipeName,
        Guid processId,
        Guid? deviceId,
        string version,
        CancellationToken cancellationToken = default);
}