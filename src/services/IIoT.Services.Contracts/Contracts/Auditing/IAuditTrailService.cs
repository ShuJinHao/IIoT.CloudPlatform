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
}
