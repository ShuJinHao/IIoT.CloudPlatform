using MediatR;

namespace IIoT.SharedKernel.Domain;

public interface IDomainEvent : INotification;

public abstract class BaseEntity<TId> : IEntity<TId>, IAggregateRoot<TId>
{
    public TId Id { get; protected init; } = default!;

    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
