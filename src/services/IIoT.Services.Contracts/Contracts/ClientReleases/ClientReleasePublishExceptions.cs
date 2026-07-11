namespace IIoT.Services.Contracts;

public abstract class ClientReleasePublishException(
    string safeMessage,
    string problemCode)
    : Exception(safeMessage)
{
    public string SafeMessage { get; } = safeMessage;

    public string ProblemCode { get; } = problemCode;
}

public sealed class ClientReleasePublishConflictException()
    : ClientReleasePublishException(PublicMessage, Code)
{
    public const string PublicMessage = "目标客户端版本已存在或发布状态与本次请求不一致。";
    public const string Code = "client_release_publish_conflict";
}

public sealed class ClientReleaseCommitUnknownException()
    : ClientReleasePublishException(PublicMessage, Code)
{
    public const string PublicMessage = "客户端版本提交结果暂时无法确认，请勿重复发布并联系管理员核验。";
    public const string Code = "client_release_commit_unknown";
}

public sealed class ClientReleasePublishUnavailableException()
    : ClientReleasePublishException(PublicMessage, Code)
{
    public const string PublicMessage = "客户端版本发布服务暂时不可用，请稍后重试。";
    public const string Code = "client_release_publish_unavailable";
}
