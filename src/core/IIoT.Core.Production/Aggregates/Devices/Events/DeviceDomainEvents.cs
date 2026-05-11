using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.Devices.Events;

public sealed record DeviceRegisteredDomainEvent(
    Guid DeviceId,
    string DeviceName,
    string Code,
    Guid ProcessId) : IDomainEvent;

public sealed record DeviceRenamedDomainEvent(
    Guid DeviceId,
    string DeviceName,
    string Code,
    Guid ProcessId) : IDomainEvent;

public sealed record DeviceDeletedDomainEvent(
    Guid DeviceId,
    string Code,
    Guid ProcessId) : IDomainEvent;
