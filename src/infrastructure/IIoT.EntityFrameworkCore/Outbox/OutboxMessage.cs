using System.Text.Json;
using IIoT.Services.Contracts;
using IIoT.SharedKernel.Domain;

namespace IIoT.EntityFrameworkCore.Outbox;

public sealed class OutboxMessage
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private OutboxMessage()
    {
    }

    public Guid Id { get; private init; }

    public OutboxMessageKind MessageKind { get; private set; } = OutboxMessageKind.DomainEvent;

    public string EventType { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    public DateTimeOffset? LastAttemptedAtUtc { get; private set; }

    public DateTimeOffset? AbandonedAtUtc { get; private set; }

    public int AttemptCount { get; private set; }

    public string? LastError { get; private set; }

    public bool IsProcessed => ProcessedAtUtc.HasValue;

    public bool IsAbandoned => AbandonedAtUtc.HasValue;

    public static OutboxMessage FromDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var eventType = domainEvent.GetType().AssemblyQualifiedName
                        ?? throw new InvalidOperationException(
                            $"Unable to resolve assembly-qualified name for domain event type {domainEvent.GetType().FullName}.");

        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageKind = OutboxMessageKind.DomainEvent,
            EventType = eventType,
            Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), SerializerOptions),
            OccurredAtUtc = DateTimeOffset.UtcNow
        };
    }

    public static OutboxMessage FromIntegrationEvent(IIntegrationEvent integrationEvent)
        => FromIntegrationEvent(
            integrationEvent,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

    public static OutboxMessage FromIntegrationEvent(
        IIntegrationEvent integrationEvent,
        Guid messageId,
        DateTimeOffset fallbackOccurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var eventType = integrationEvent.GetType().AssemblyQualifiedName
                        ?? throw new InvalidOperationException(
                            $"Unable to resolve assembly-qualified name for integration event type {integrationEvent.GetType().FullName}.");

        return new OutboxMessage
        {
            Id = messageId,
            MessageKind = OutboxMessageKind.IntegrationEvent,
            EventType = eventType,
            Payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), SerializerOptions),
            OccurredAtUtc = NormalizePostgresTimestamp(
                integrationEvent.OccurredAtUtc == default
                    ? fallbackOccurredAtUtc
                    : integrationEvent.OccurredAtUtc)
        };
    }

    internal static DateTimeOffset NormalizePostgresTimestamp(DateTimeOffset value)
    {
        var utcValue = value.ToUniversalTime();
        return new DateTimeOffset(
            utcValue.Ticks - utcValue.Ticks % TimeSpan.TicksPerMicrosecond,
            TimeSpan.Zero);
    }

    internal static bool JsonPayloadEquals(string persisted, string target)
    {
        try
        {
            using var persistedDocument = JsonDocument.Parse(persisted);
            using var targetDocument = JsonDocument.Parse(target);
            return JsonElement.DeepEquals(
                persistedDocument.RootElement,
                targetDocument.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public IDomainEvent DeserializeDomainEvent()
    {
        var type = Type.GetType(EventType, throwOnError: false);
        if (type is null)
        {
            throw new InvalidOperationException($"Unable to resolve outbox event type '{EventType}'.");
        }

        var domainEvent = JsonSerializer.Deserialize(Payload, type, SerializerOptions);
        if (domainEvent is not IDomainEvent typedDomainEvent)
        {
            throw new InvalidOperationException(
                $"Outbox payload type '{EventType}' is not a valid domain event.");
        }

        return typedDomainEvent;
    }

    public IIntegrationEvent DeserializeIntegrationEvent()
    {
        var type = Type.GetType(EventType, throwOnError: false);
        if (type is null)
        {
            throw new InvalidOperationException($"Unable to resolve outbox event type '{EventType}'.");
        }

        var integrationEvent = JsonSerializer.Deserialize(Payload, type, SerializerOptions);
        if (integrationEvent is not IIntegrationEvent typedIntegrationEvent)
        {
            throw new InvalidOperationException(
                $"Outbox payload type '{EventType}' is not a valid integration event.");
        }

        return typedIntegrationEvent;
    }

    public void MarkProcessed()
    {
        ProcessedAtUtc = DateTimeOffset.UtcNow;
        LastAttemptedAtUtc = ProcessedAtUtc;
        AbandonedAtUtc = null;
        AttemptCount++;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        MarkFailed(error, int.MaxValue);
    }

    public void MarkFailed(string error, int maxAttempts)
    {
        LastAttemptedAtUtc = DateTimeOffset.UtcNow;
        AttemptCount++;
        LastError = string.IsNullOrWhiteSpace(error)
            ? "Unknown outbox dispatch failure."
            : error;

        if (AttemptCount >= maxAttempts)
        {
            AbandonedAtUtc = LastAttemptedAtUtc;
        }
    }
}
