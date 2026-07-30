using IIoT.SharedKernel.Domain;

namespace IIoT.Core.MasterData.Aggregates.MfgProcesses.Events;

public sealed record MfgProcessCreatedDomainEvent(
    Guid ProcessId,
    string ProcessCode,
    string ProcessName) : IDomainEvent;

public sealed record MfgProcessRenamedDomainEvent(
    Guid ProcessId,
    string OldProcessCode,
    string NewProcessCode,
    string OldProcessName,
    string NewProcessName) : IDomainEvent;

public sealed record MfgProcessDeletedDomainEvent(
    Guid ProcessId,
    string ProcessCode,
    string ProcessName) : IDomainEvent;
