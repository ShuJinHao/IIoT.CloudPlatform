using System.Data;
using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Services.Contracts.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

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
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            ObserveSnapshotAsync,
            cancellationToken);

        async Task<EmployeeMutationObservation> ObserveSnapshotAsync(
            CancellationToken strategyCancellationToken)
        {
            var isolationLevel = context.Database.IsNpgsql()
                ? IsolationLevel.RepeatableRead
                : IsolationLevel.Serializable;
            await using var snapshot =
                await context.Database.BeginTransactionAsync(
                    isolationLevel,
                    strategyCancellationToken);

            var employeeRows = await (
                    from candidate in context.Employees.AsNoTracking()
                    where candidate.Id == employeeId
                    join access in context.Set<EmployeeDeviceAccess>().AsNoTracking()
                        on candidate.Id equals access.EmployeeId into accesses
                    from access in accesses.DefaultIfEmpty()
                    select new
                    {
                        candidate.EmployeeNo,
                        candidate.RealName,
                        candidate.IsActive,
                        candidate.RowVersion,
                        DeviceId = access == null
                            ? (Guid?)null
                            : access.DeviceId
                    })
                .ToListAsync(strategyCancellationToken);
            var employee = employeeRows.FirstOrDefault();
            var employeeDeviceIds = employeeRows
                .Where(row => row.DeviceId.HasValue)
                .Select(row => row.DeviceId!.Value)
                .Distinct()
                .OrderBy(deviceId => deviceId)
                .ToArray();
            var accountRows = await (
                    from user in context.Users.AsNoTracking()
                    where user.Id == employeeId
                    join userRole in context.UserRoles.AsNoTracking()
                        on user.Id equals userRole.UserId into userRoles
                    from userRole in userRoles.DefaultIfEmpty()
                    join role in context.Roles.AsNoTracking()
                        on userRole.RoleId equals role.Id into roles
                    from role in roles.DefaultIfEmpty()
                    select new
                    {
                        user.UserName,
                        user.IsEnabled,
                        user.SecurityStamp,
                        RoleName = role == null ? null : role.Name
                    })
                .ToListAsync(strategyCancellationToken);
            var account = accountRows.FirstOrDefault();
            var accountRoles = accountRows
                .Select(row => row.RoleName)
                .Where(roleName => !string.IsNullOrWhiteSpace(roleName))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(roleName => roleName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var subject = employeeId.ToString();
            var activeSessionMarkers = context.RefreshTokenSessions
                .AsNoTracking()
                .Where(session =>
                    session.ActorType == IIoTClaimTypes.HumanActor
                    && session.SubjectId == employeeId
                    && !session.RevokedAtUtc.HasValue)
                .Select(_ => 1)
                .Concat(
                    context.OpenIddictTokens
                        .AsNoTracking()
                        .Where(token =>
                            token.Subject == subject
                            && token.Status != OpenIddictConstants.Statuses.Revoked)
                        .Select(_ => 1))
                .Concat(
                    context.OpenIddictAuthorizations
                        .AsNoTracking()
                        .Where(authorization =>
                            authorization.Subject == subject
                            && authorization.Status
                            != OpenIddictConstants.Statuses.Revoked)
                        .Select(_ => 1));
            var hasActiveHumanSessions = await activeSessionMarkers
                .AnyAsync(strategyCancellationToken);

            var observation = new EmployeeMutationObservation(
                EmployeeExists: employee is not null,
                EmployeeIsActive: employee?.IsActive ?? false,
                AccountExists: account is not null,
                AccountIsEnabled: account?.IsEnabled ?? false,
                AccountSecurityStamp: account?.SecurityStamp,
                Roles: accountRoles,
                EmployeeNo: employee?.EmployeeNo,
                EmployeeRealName: employee?.RealName,
                EmployeeRowVersion: employee?.RowVersion,
                EmployeeDeviceIds: employeeDeviceIds,
                AccountEmployeeNo: account?.UserName,
                HasActiveHumanSessions: hasActiveHumanSessions);
            await snapshot.CommitAsync(strategyCancellationToken);
            return observation;
        }
    }
}
