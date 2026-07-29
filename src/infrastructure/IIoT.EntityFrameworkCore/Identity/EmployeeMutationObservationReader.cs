using IIoT.Services.Contracts.Identity;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.Identity;

public sealed class EmployeeMutationObservationReader(
    DbContextOptions<IIoTDbContext> options)
    : IEmployeeMutationObservationReader
{
    public async Task<EmployeeMutationObservation> ObserveAsync(
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        await using var context = new IIoTDbContext(options);

        var employeeActive = await context.Employees
            .AsNoTracking()
            .Where(employee => employee.Id == employeeId)
            .Select(employee => (bool?)employee.IsActive)
            .SingleOrDefaultAsync(cancellationToken);
        var account = await context.Users
            .AsNoTracking()
            .Where(user => user.Id == employeeId)
            .Select(user => new
            {
                user.IsEnabled,
                user.SecurityStamp
            })
            .SingleOrDefaultAsync(cancellationToken);
        var roles = account is null
            ? []
            : await context.UserRoles
                .AsNoTracking()
                .Where(userRole => userRole.UserId == employeeId)
                .Join(
                    context.Roles.AsNoTracking(),
                    userRole => userRole.RoleId,
                    role => role.Id,
                    (_, role) => role.Name!)
                .Where(roleName => roleName != null)
                .Distinct()
                .OrderBy(roleName => roleName)
                .ToArrayAsync(cancellationToken);

        return new EmployeeMutationObservation(
            EmployeeExists: employeeActive.HasValue,
            EmployeeIsActive: employeeActive.GetValueOrDefault(),
            AccountExists: account is not null,
            AccountIsEnabled: account?.IsEnabled ?? false,
            AccountSecurityStamp: account?.SecurityStamp,
            Roles: roles);
    }
}
