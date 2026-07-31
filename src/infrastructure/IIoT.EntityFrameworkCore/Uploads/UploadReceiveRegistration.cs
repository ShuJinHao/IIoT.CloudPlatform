namespace IIoT.EntityFrameworkCore.Uploads;

public sealed class UploadReceiveRegistration
{
    private UploadReceiveRegistration()
    {
    }

    public Guid Id { get; private init; }

    public Guid DeviceId { get; private set; }

    public string MessageType { get; private set; } = string.Empty;

    public string? RequestId { get; private set; }

    public string DeduplicationKey { get; private set; } = string.Empty;

    public Guid OutboxMessageId { get; private set; }

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public DateTimeOffset LastSeenAtUtc { get; private set; }

    public int SeenCount { get; private set; }

    public static UploadReceiveRegistration Create(
        Guid deviceId,
        string messageType,
        string? requestId,
        string deduplicationKey,
        Guid outboxMessageId)
        => Create(
            Guid.NewGuid(),
            deviceId,
            messageType,
            requestId,
            deduplicationKey,
            outboxMessageId,
            DateTimeOffset.UtcNow);

    public static UploadReceiveRegistration Create(
        Guid registrationId,
        Guid deviceId,
        string messageType,
        string? requestId,
        string deduplicationKey,
        Guid outboxMessageId,
        DateTimeOffset receivedAtUtc)
    {
        return new UploadReceiveRegistration
        {
            Id = registrationId,
            DeviceId = deviceId,
            MessageType = messageType,
            RequestId = requestId,
            DeduplicationKey = deduplicationKey,
            OutboxMessageId = outboxMessageId,
            ReceivedAtUtc = receivedAtUtc,
            LastSeenAtUtc = receivedAtUtc,
            SeenCount = 1
        };
    }

    public void MarkSeen(DateTimeOffset seenAtUtc)
    {
        if (seenAtUtc <= LastSeenAtUtc)
        {
            return;
        }

        LastSeenAtUtc = seenAtUtc;
        SeenCount++;
    }
}
