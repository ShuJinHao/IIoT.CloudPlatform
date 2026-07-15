namespace IIoT.Services.Contracts;

/// <summary>
/// Exposes the stable persisted outbox identity while a domain event is dispatched.
/// Access outside an outbox dispatch is invalid and must fail closed.
/// </summary>
public interface IDomainEventDispatchContext
{
    Guid MessageId { get; }
}
