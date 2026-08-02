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

    public string? ContentFingerprint { get; private set; }

    public Guid OutboxMessageId { get; private set; }

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public DateTimeOffset LastSeenAtUtc { get; private set; }

    public int SeenCount { get; private set; }

    public static UploadReceiveRegistration Create(
        Guid deviceId,
        string messageType,
        string? requestId,
        string deduplicationKey,
        Guid outboxMessageId,
        string? contentFingerprint = null)
        => Create(
            Guid.NewGuid(),
            deviceId,
            messageType,
            requestId,
            deduplicationKey,
            outboxMessageId,
            DateTimeOffset.UtcNow,
            contentFingerprint);

    public static UploadReceiveRegistration Create(
        Guid registrationId,
        Guid deviceId,
        string messageType,
        string? requestId,
        string deduplicationKey,
        Guid outboxMessageId,
        DateTimeOffset receivedAtUtc,
        string? contentFingerprint = null)
    {
        return new UploadReceiveRegistration
        {
            Id = registrationId,
            DeviceId = deviceId,
            MessageType = messageType,
            RequestId = requestId,
            DeduplicationKey = deduplicationKey,
            ContentFingerprint = contentFingerprint,
            OutboxMessageId = outboxMessageId,
            ReceivedAtUtc = receivedAtUtc,
            LastSeenAtUtc = receivedAtUtc,
            SeenCount = 1
        };
    }

    public void MarkSeen(DateTimeOffset seenAtUtc)
    {
        if (seenAtUtc > LastSeenAtUtc)
        {
            LastSeenAtUtc = seenAtUtc;
        }

        SeenCount++;
    }

    public void BackfillContentFingerprint(string contentFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentFingerprint);
        if (contentFingerprint.Length != 64)
            throw new ArgumentOutOfRangeException(nameof(contentFingerprint));
        if (ContentFingerprint is not null
            && !string.Equals(ContentFingerprint, contentFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Upload content fingerprint is immutable.");
        }

        ContentFingerprint = contentFingerprint;
    }
}
