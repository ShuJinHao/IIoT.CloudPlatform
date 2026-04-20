namespace IIoT.Services.Contracts.Identity;

public interface IEmployeeLookupService
{
    Task<EmployeeLookupDto?> GetByIdAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default);
}

public record EmployeeLookupDto(
    Guid Id,
    string EmployeeNo,
    string RealName,
    bool IsActive,
    IReadOnlyList<Guid> DeviceIds);
