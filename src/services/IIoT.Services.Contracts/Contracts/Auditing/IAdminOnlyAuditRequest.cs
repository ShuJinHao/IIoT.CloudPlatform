namespace IIoT.Services.Contracts.Auditing;

/// <summary>
/// 为 AdminOnly 管道提供最小、脱敏的拒绝审计元数据。
/// 命令中的密码、自由文本或其它敏感内容不得通过此契约进入审计。
/// </summary>
public interface IAdminOnlyAuditRequest
{
    string AdminAuditOperationType { get; }

    string AdminAuditTargetType { get; }

    string AdminAuditTargetIdOrKey { get; }
}
