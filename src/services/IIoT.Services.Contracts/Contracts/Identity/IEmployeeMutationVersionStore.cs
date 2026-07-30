namespace IIoT.Services.Contracts.Identity;

/// <summary>
/// Advances the PostgreSQL row version of an employee without changing
/// business fields. Device-access mutations use this as their aggregate CAS
/// marker because those writes otherwise only update the child table.
/// </summary>
public interface IEmployeeMutationVersionStore
{
    Task<uint?> TryAdvanceAsync(
        Guid employeeId,
        uint expectedRowVersion,
        CancellationToken cancellationToken);
}
