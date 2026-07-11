namespace IIoT.Services.Contracts;

public sealed class DistributedLockConflictException()
    : Exception(PublicMessage)
{
    public const string PublicMessage = "请求正在由其他操作处理，请稍后重试。";
}

public sealed class DistributedLockUnavailableException()
    : Exception(PublicMessage)
{
    public const string PublicMessage = "请求协调服务暂时不可用，请稍后重试。";
}

public sealed class DistributedLockOwnershipLostException()
    : Exception(PublicMessage)
{
    public const string PublicMessage = "请求协调状态已失效，请稍后重试。";
}
