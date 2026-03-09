using System.Text.Json;
using IIoT.Services.Common.Contracts;
using Microsoft.Extensions.Caching.Distributed;

namespace IIoT.Infrastructure.Caching;

/// <summary>
/// Redis 分布式缓存实现类
/// </summary>
/// <remarks>
/// 真正对接底层 IDistributedCache 的地方，由 Aspire 提供具体的 Redis 连接池注入。
/// </remarks>
public class RedisCacheService(IDistributedCache distributedCache) : ICacheService
{
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var cachedData = await distributedCache.GetStringAsync(key, cancellationToken);

        if (string.IsNullOrEmpty(cachedData))
            return default;

        return JsonSerializer.Deserialize<T>(cachedData);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? absoluteExpireTime = null, CancellationToken cancellationToken = default)
    {
        var options = new DistributedCacheEntryOptions();

        if (absoluteExpireTime.HasValue)
        {
            options.AbsoluteExpirationRelativeToNow = absoluteExpireTime.Value;
        }

        var serializedData = JsonSerializer.Serialize(value);

        await distributedCache.SetStringAsync(key, serializedData, options, cancellationToken);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await distributedCache.RemoveAsync(key, cancellationToken);
    }
}