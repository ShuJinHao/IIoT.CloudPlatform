using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.Identity;

public sealed class EmployeeLookupService(IIoTDbContext dbContext) : IEmployeeLookupService
{
    public async Task<EmployeeLookupDto?> GetByIdAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Employees
            .AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => new EmployeeLookupDto(
                e.Id,
                e.EmployeeNo,
                e.RealName,
                e.IsActive,
                e.DeviceAccesses.Select(d => d.DeviceId).ToList()))
            .FirstOrDefaultAsync(cancellationToken);
    }
}

