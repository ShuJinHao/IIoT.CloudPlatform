namespace IIoT.Services.Contracts;

/// <summary>
/// 分布式锁服务契约接口
/// </summary>
public interface IDistributedLockService
{
    /// <summary>
    /// 尝试获取分布式锁，成功则返回可观测所有权丢失的租约。
    /// </summary>
    /// <param name="resource">锁资源名称（唯一 key）</param>
    /// <param name="acquireTimeout">
    /// 从发起获取到返回结果的总时间预算，默认 10 秒。
    /// 传入零时只执行一次不等待尝试。
    /// </param>
    /// <param name="cancellationToken"></param>
    Task<IDistributedLockLease> AcquireAsync(
        string resource,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 分布式锁租约。租约所有权一旦丢失，<see cref="OwnershipLost"/> 必须永久取消。
/// </summary>
public interface IDistributedLockLease : IAsyncDisposable
{
    CancellationToken OwnershipLost { get; }
}
