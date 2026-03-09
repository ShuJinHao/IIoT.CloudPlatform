namespace IIoT.Services.Common.Contracts;

/// <summary>
/// 全局分布式缓存服务契约接口 (防腐层)
/// </summary>
/// <remarks>
/// 架构意义：屏蔽底层具体的缓存中间件 (如 Redis、Memcached 或 MemoryCache)。
/// 上层业务和 EF Core 数据层只依赖此接口进行缓存的读写，实现极度解耦。
/// </remarks>
public interface ICacheService
{
    /// <summary>
    /// 获取缓存数据并反序列化为指定类型
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 将对象序列化并写入缓存
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="value">要缓存的对象</param>
    /// <param name="absoluteExpireTime">绝对过期时间 (如果不传则使用底层默认策略)</param>
    Task SetAsync<T>(string key, T value, TimeSpan? absoluteExpireTime = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除指定缓存
    /// </summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}