using IIoT.Services.Contracts.Identity;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.Identity;

public sealed class EmployeeMutationVersionStore(IIoTDbContext dbContext)
    : IEmployeeMutationVersionStore
{
    public async Task<uint?> TryAdvanceAsync(
        Guid employeeId,
        uint expectedRowVersion,
        CancellationToken cancellationToken)
    {
        var affected = await dbContext.Employees
            .Where(employee =>
                employee.Id == employeeId
                && employee.RowVersion == expectedRowVersion)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    employee => employee.IsActive,
                    employee => employee.IsActive),
                cancellationToken);
        if (affected != 1)
        {
            return null;
        }

        return await dbContext.Employees
            .AsNoTracking()
            .Where(employee => employee.Id == employeeId)
            .Select(employee => (uint?)employee.RowVersion)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
