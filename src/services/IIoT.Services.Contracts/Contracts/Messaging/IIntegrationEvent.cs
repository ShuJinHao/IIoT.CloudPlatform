namespace IIoT.Services.Contracts;

public interface IIntegrationEvent
{
    Guid EventId { get; init; }

    DateTimeOffset OccurredAtUtc { get; init; }

    int SchemaVersion { get; init; }
}
