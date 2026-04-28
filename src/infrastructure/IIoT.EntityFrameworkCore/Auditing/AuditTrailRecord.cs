using IIoT.Services.Contracts.Auditing;

namespace IIoT.EntityFrameworkCore.Auditing;

public sealed class AuditTrailRecord
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid? ActorUserId { get; private set; }

    public string? ActorEmployeeNo { get; private set; }

    public string OperationType { get; private set; } = string.Empty;

    public string TargetType { get; private set; } = string.Empty;

    public string TargetIdOrKey { get; private set; } = string.Empty;

    public DateTime ExecutedAtUtc { get; private set; }

    public bool Succeeded { get; private set; }

    public string Summary { get; private set; } = string.Empty;

    public string? FailureReason { get; private set; }

    private AuditTrailRecord()
    {
    }

    public static AuditTrailRecord FromEntry(AuditTrailEntry entry)
    {
        return new AuditTrailRecord
        {
            ActorUserId = entry.ActorUserId,
            ActorEmployeeNo = entry.ActorEmployeeNo,
            OperationType = entry.OperationType,
            TargetType = entry.TargetType,
            TargetIdOrKey = entry.TargetIdOrKey,
            ExecutedAtUtc = entry.ExecutedAtUtc,
            Succeeded = entry.Succeeded,
            Summary = entry.Summary,
            FailureReason = entry.FailureReason
        };
    }
}
