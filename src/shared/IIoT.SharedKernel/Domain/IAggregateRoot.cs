namespace IIoT.SharedKernel.Domain;

public interface IAggregateRoot : IEntity;

public interface IAggregateRoot<TId> : IEntity<TId>;