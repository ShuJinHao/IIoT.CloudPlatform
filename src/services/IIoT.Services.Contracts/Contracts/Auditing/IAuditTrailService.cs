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
    string? FailureReason = null);

public interface IAuditTrailService
{
    Task TryWriteAsync(
        AuditTrailEntry entry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 写入审计并返回是否真正持久化成功。默认实现回退到 <see cref="TryWriteAsync"/> 并视为成功，
    /// 不破坏既有实现；需要在“审计写稳后才能推进”的链路（如永久删除两阶段收敛）使用。
    /// </summary>
    async Task<bool> TryWriteConfirmedAsync(
        AuditTrailEntry entry,
        CancellationToken cancellationToken = default)
    {
        await TryWriteAsync(entry, cancellationToken);
        return true;
    }
}
