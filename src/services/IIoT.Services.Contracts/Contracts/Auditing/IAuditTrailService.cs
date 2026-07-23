namespace IIoT.Services.Contracts.Auditing;

public sealed record AuditTrailEntry(
    Guid? ActorUserId,
    string? ActorEmployeeNo,
    string OperationType,
    string TargetType,
    string TargetIdOrKey,
    DateTime ExecutedAtUtc,
    bool Succeeded,
    string Summary,
    string? FailureReason = null,
    string? IdempotencyKey = null);

public interface IAuditTrailService
{
    Task TryWriteAsync(
        AuditTrailEntry entry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 写入审计并返回是否真正持久化成功。需要在“审计写稳后才能推进”的链路
    /// （如永久删除两阶段收敛）使用；实现必须明确确认持久化结果，不能用默认成功掩盖失败。
    /// </summary>
    Task<bool> TryWriteConfirmedAsync(
        AuditTrailEntry entry,
        CancellationToken cancellationToken = default);
}
