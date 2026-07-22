namespace IIoT.ProductionService.ClientReleases;

/// <summary>
/// 发布域分布式锁契约。硬删除、删除重试、启动恢复与发布共享同一把锁，
/// 公开给宿主启动恢复服务使用，避免复制锁资源字符串。
/// </summary>
public static class ClientReleasePublishLock
{
    public const string Resource = "iiot:lock:client-release:publish";

    public const int AcquireTimeoutSeconds = 5;
}
