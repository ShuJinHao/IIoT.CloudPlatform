namespace IIoT.EntityFrameworkCore.Outbox;

public enum OutboxMessageKind
{
    DomainEvent = 0,
    IntegrationEvent = 1
}
