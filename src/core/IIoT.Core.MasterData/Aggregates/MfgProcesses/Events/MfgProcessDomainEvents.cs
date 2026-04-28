using IIoT.SharedKernel.Domain;

namespace IIoT.Core.MasterData.Aggregates.MfgProcesses.Events;

public sealed record MfgProcessRenamedDomainEvent(
    Guid ProcessId,
    string OldProcessCode,
    string NewProcessCode,
    string OldProcessName,
    string NewProcessName) : IDomainEvent;
