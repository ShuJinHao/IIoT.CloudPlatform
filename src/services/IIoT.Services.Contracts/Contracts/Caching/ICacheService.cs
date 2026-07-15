namespace IIoT.Services.Contracts;

/// <summary>
/// 全局分布式缓存服务契约接口 (防腐层)
/// </summary>
/// <remarks>
/// 架构意义：屏蔽底层具体的缓存中间件 (如 Redis、Memcached 或 MemoryCache)。
/// 仅用于允许缓存故障降级的普通值缓存。权限、设备访问范围、设备身份、
/// 分布式租约、幂等登记和 Outbox 等安全/一致性敏感链路不得使用本接口。
/// </remarks>
public interface ICacheService
{
    /// <summary>
    /// 原子地读取缓存；未命中时仅由单个调用者执行回源工厂，并按调用方显式策略决定是否回填。
    /// </summary>
    Task<T?> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        Func<T?, bool> shouldCache,
        TimeSpan absoluteExpireTime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除指定缓存
    /// </summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量删除匹配通配符模式的所有缓存键，用于无法枚举精确 Key 的场景（如带日期范围的产能缓存）。
    /// 模式语法与 Redis KEYS 一致，例如 "iiot:capacity:summary:v1:*"
    /// </summary>
    Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);
}
