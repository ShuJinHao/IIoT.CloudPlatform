namespace IIoT.EntityFrameworkCore.Outbox;

public sealed record OutboxDispatchResult(
    int ScannedCount,
    int SucceededCount,
    int FailedCount,
    int PendingBacklogCount,
    int AbandonedCount,
    string? LastFailureSummary);
