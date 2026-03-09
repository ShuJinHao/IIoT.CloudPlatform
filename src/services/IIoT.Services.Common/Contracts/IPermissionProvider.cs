namespace IIoT.Services.Common.Contracts;

/// <summary>
/// 角色权限提供者契约接口 (遵循依赖倒置原则 DIP)
/// </summary>
/// <remarks>
/// 核心职责：为系统的鉴权管道 (AuthorizationBehavior) 提供动态的权限数据源。
/// 架构意义：将业务层与基础设施层（Redis缓存、PostgreSQL数据库、Identity框架）彻底物理隔离。
/// 业务层无需、也不应知道底层是如何实现缓存旁路 (Cache-Aside) 机制的。
/// </remarks>
public interface IPermissionProvider
{
    /// <summary>
    /// 根据角色名称，动态获取该角色所拥有的全部权限标识集合
    /// </summary>
    /// <param name="roleName">系统角色名称 (例如: "Admin", "Operator", "Engineer")</param>
    /// <param name="cancellationToken">异步操作取消令牌 (工业级高并发场景下的标准规范，便于及时熔断)</param>
    /// <returns>
    /// 返回权限标识的字符串集合 (例如: ["Identity.CreateRole", "Recipe.Create"])。
    /// 如果该角色没有任何权限，或者该角色不存在，应严格返回空集合 (Empty List) 而不是 null，防止上层出现空引用异常。
    /// </returns>
    Task<IList<string>> GetPermissionsAsync(string roleName, CancellationToken cancellationToken = default);
}