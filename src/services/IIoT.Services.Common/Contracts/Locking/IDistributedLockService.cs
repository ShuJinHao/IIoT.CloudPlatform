namespace IIoT.Services.Common.Contracts;

/// <summary>
/// 分布式锁服务契约接口
/// </summary>
public interface IDistributedLockService
{
    /// <summary>
    /// 尝试获取分布式锁，成功则返回锁句柄（using 释放），超时则抛出 TimeoutException
    /// </summary>
    /// <param name="resource">锁资源名称（唯一 key）</param>
    /// <param name="acquireTimeout">等待获取锁的最长时间，默认 10 秒</param>
    /// <param name="cancellationToken"></param>
    Task<IAsyncDisposable> AcquireAsync(string resource, TimeSpan? acquireTimeout = null, CancellationToken cancellationToken = default);
}
