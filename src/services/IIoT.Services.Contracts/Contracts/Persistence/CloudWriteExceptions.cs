namespace IIoT.Services.Contracts;

public abstract class CloudWriteException(
    string safeMessage,
    string problemCode)
    : Exception(safeMessage)
{
    public string SafeMessage { get; } = safeMessage;

    public string ProblemCode { get; } = problemCode;
}

public sealed class CloudWriteConflictException()
    : CloudWriteException(PublicMessage, Code)
{
    public const string PublicMessage = "云端写入状态已发生变化，请刷新后重试。";
    public const string Code = "cloud_write_conflict";
}

public sealed class CloudWriteCommitUnknownException()
    : CloudWriteException(PublicMessage, Code)
{
    public const string PublicMessage = "云端写入提交结果暂时无法确认，请勿重复操作并联系管理员核验。";
    public const string Code = "cloud_write_commit_unknown";
}
