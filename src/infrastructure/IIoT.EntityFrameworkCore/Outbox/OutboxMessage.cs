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

    public int AttemptCount { get; private set; }

    public string? LastError { get; private set; }

    public bool IsProcessed => ProcessedAtUtc.HasValue;

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
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var eventType = integrationEvent.GetType().AssemblyQualifiedName
                        ?? throw new InvalidOperationException(
                            $"Unable to resolve assembly-qualified name for integration event type {integrationEvent.GetType().FullName}.");

        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            MessageKind = OutboxMessageKind.IntegrationEvent,
            EventType = eventType,
            Payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), SerializerOptions),
            OccurredAtUtc = integrationEvent.OccurredAtUtc == default
                ? DateTimeOffset.UtcNow
                : integrationEvent.OccurredAtUtc
        };
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
        AttemptCount++;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        LastAttemptedAtUtc = DateTimeOffset.UtcNow;
        AttemptCount++;
        LastError = string.IsNullOrWhiteSpace(error)
            ? "Unknown outbox dispatch failure."
            : error;
    }
}
