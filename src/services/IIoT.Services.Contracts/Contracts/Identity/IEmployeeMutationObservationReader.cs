namespace IIoT.Services.Contracts.Identity;

public sealed record EmployeeMutationObservation(
    bool EmployeeExists,
    bool EmployeeIsActive,
    bool AccountExists,
    bool AccountIsEnabled,
    string? AccountSecurityStamp,
    IReadOnlyList<string> Roles,
    string? EmployeeNo = null,
    string? EmployeeRealName = null,
    uint? EmployeeRowVersion = null,
    IReadOnlyList<Guid>? EmployeeDeviceIds = null,
    string? AccountEmployeeNo = null,
    bool HasActiveHumanSessions = false);

/// <summary>
/// Reads employee and identity mutation state through a newly-created persistence context
/// and one consistent database snapshot.
/// This port is reserved for resolving a transaction whose commit acknowledgement was lost.
/// </summary>
public interface IEmployeeMutationObservationReader
{
    Task<EmployeeMutationObservation> ObserveAsync(
        Guid employeeId,
        CancellationToken cancellationToken);
}
