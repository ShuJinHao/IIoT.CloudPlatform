using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Employees.Aggregates.Employees.Events;

public sealed record EmployeeOnboardedDomainEvent(
    Guid EmployeeId,
    string EmployeeNo,
    string RealName) : IDomainEvent;

public sealed record EmployeeRenamedDomainEvent(
    Guid EmployeeId,
    string EmployeeNo,
    string RealName) : IDomainEvent;

public sealed record EmployeeActivatedDomainEvent(Guid EmployeeId) : IDomainEvent;

public sealed record EmployeeDeactivatedDomainEvent(Guid EmployeeId) : IDomainEvent;

public sealed record EmployeeTerminatedDomainEvent(
    Guid EmployeeId,
    string EmployeeNo) : IDomainEvent;
