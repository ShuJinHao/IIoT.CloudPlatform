using IIoT.Services.Common.Contracts;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;

namespace IIoT.Infrastructure.Caching;

/// <summary>
/// Redis 分布式缓存实现类 - 基于 FusionCache（L1 内存 + L2 Redis + Backplane 多实例同步）
/// </summary>
public class RedisCacheService(
    IFusionCache fusionCache,
    IConnectionMultiplexer redis,
    ILogger<RedisCacheService> logger) : ICacheService
{
    private readonly IFusionCache _fusionCache = fusionCache;
    private readonly IConnectionMultiplexer _redis = redis;
    private readonly ILogger<RedisCacheService> _logger = logger;

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _fusionCache.TryGetAsync<T>(key, token: cancellationToken);
            return result.HasValue ? result.Value : default;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis cache read failed for key {Key}", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? absoluteExpireTime = null, CancellationToken cancellationToken = default)
    {
        if (value == null)
        {
            await RemoveAsync(key, cancellationToken);
            return;
        }

        var duration = absoluteExpireTime ?? TimeSpan.FromMinutes(5);

        try
        {
            await _fusionCache.SetAsync(key, value, duration, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis cache write failed for key {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _fusionCache.RemoveAsync(key, token: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis cache remove failed for key {Key}", key);
        }
    }

    /// <inheritdoc/>
    public async Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        try
        {
            // 遍历所有 Redis 服务端节点（Cluster/Sentinel 兼容）
            foreach (var endpoint in _redis.GetEndPoints())
            {
                try
                {
                    var server = _redis.GetServer(endpoint);
                    if (!server.IsConnected) continue;

                    // SCAN 扫描匹配的 Key，通过 FusionCache 删除（同时清 L1 + L2 + 通知 Backplane）
                    await foreach (var key in server.KeysAsync(pattern: pattern).WithCancellation(cancellationToken))
                    {
                        await _fusionCache.RemoveAsync(key.ToString(), token: cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Redis cache remove-by-pattern failed for pattern {Pattern} on endpoint {Endpoint}",
                        pattern,
                        endpoint);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis cache remove-by-pattern failed for pattern {Pattern}", pattern);
        }
    }
}
