using IIoT.Services.Common.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace IIoT.Infrastructure.Locking;

/// <summary>
/// 基于 Redis SET NX EX + Lua 原子校验的分布式锁实现。
/// 获取锁后会按配置自动续租，避免长事务执行期间锁提前过期。
/// </summary>
public sealed class RedisDistributedLockService(
    IConnectionMultiplexer redis,
    IOptions<DistributedLockOptions> options,
    ILogger<RedisDistributedLockService> logger) : IDistributedLockService
{
    private const string UnlockScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then " +
        "  return redis.call('del', KEYS[1]) " +
        "else " +
        "  return 0 " +
        "end";

    private const string RenewScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then " +
        "  return redis.call('pexpire', KEYS[1], ARGV[2]) " +
        "else " +
        "  return 0 " +
        "end";

    private readonly IDatabase _db = redis.GetDatabase();
    private readonly DistributedLockOptions _options = options.Value;

    public async Task<IAsyncDisposable> AcquireAsync(
        string resource,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var leaseTtl = _options.ResolveLeaseTtl();
        var renewalCadence = _options.ResolveRenewalCadence(leaseTtl);
        var lockValue = Guid.NewGuid().ToString("N");
        var deadline = DateTime.UtcNow.Add(acquireTimeout ?? TimeSpan.FromSeconds(10));

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var acquired = await _db.StringSetAsync(
                resource,
                lockValue,
                leaseTtl,
                When.NotExists);

            if (acquired)
            {
                return new RedisLockHandle(
                    _db,
                    resource,
                    lockValue,
                    leaseTtl,
                    renewalCadence,
                    logger);
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException($"获取分布式锁超时（{acquireTimeout?.TotalSeconds ?? 10}s）：{resource}");
    }

    private sealed class RedisLockHandle : IAsyncDisposable
    {
        private readonly IDatabase _db;
        private readonly string _key;
        private readonly string _value;
        private readonly TimeSpan _leaseTtl;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _renewalCts = new();
        private readonly Task _renewalTask;

        public RedisLockHandle(
            IDatabase db,
            string key,
            string value,
            TimeSpan leaseTtl,
            TimeSpan renewalCadence,
            ILogger logger)
        {
            _db = db;
            _key = key;
            _value = value;
            _leaseTtl = leaseTtl;
            _logger = logger;
            _renewalTask = RunRenewalLoopAsync(renewalCadence);
        }

        public async ValueTask DisposeAsync()
        {
            _renewalCts.Cancel();

            try
            {
                await _renewalTask;
            }
            catch (OperationCanceledException)
            {
            }

            await _db.ScriptEvaluateAsync(
                UnlockScript,
                keys: [(RedisKey)_key],
                values: [(RedisValue)_value]);
        }

        private async Task RunRenewalLoopAsync(TimeSpan renewalCadence)
        {
            using var timer = new PeriodicTimer(renewalCadence);

            while (await timer.WaitForNextTickAsync(_renewalCts.Token))
            {
                try
                {
                    var renewed = await _db.ScriptEvaluateAsync(
                        RenewScript,
                        keys: [(RedisKey)_key],
                        values:
                        [
                            (RedisValue)_value,
                            (RedisValue)((long)_leaseTtl.TotalMilliseconds)
                        ]);

                    if ((long)renewed == 0)
                    {
                        _logger.LogWarning(
                            "Distributed lock renewal stopped because the lock was lost for {Key}.",
                            _key);
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Distributed lock renewal failed for {Key}.", _key);
                }
            }
        }
    }
}
