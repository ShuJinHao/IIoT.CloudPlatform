namespace IIoT.Services.Contracts;

public interface IUploadReceiveRegistry
{
    Task<UploadReceiveRegistrationResult> RegisterAndEnqueueAsync(
        Guid deviceId,
        string messageType,
        string? requestId,
        string deduplicationKey,
        IIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default);
}

public sealed record UploadReceiveRegistrationResult(
    bool IsDuplicate,
    Guid? OutboxMessageId)
{
    public static UploadReceiveRegistrationResult Registered(Guid outboxMessageId)
    {
        return new UploadReceiveRegistrationResult(false, outboxMessageId);
    }

    public static UploadReceiveRegistrationResult Duplicate(Guid? outboxMessageId)
    {
        return new UploadReceiveRegistrationResult(true, outboxMessageId);
    }
}
