using IIoT.Services.Contracts.Identity;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.Identity;

public sealed class CloudOidcUserProfileService(IIoTDbContext dbContext) : ICloudOidcUserProfileService
{
    public Task<CloudOidcUserProfile?> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return GetProfileByUserIdAsync(userId, cancellationToken);
    }

    public Task<CloudOidcUserProfile?> GetByEmployeeNoAsync(
        string employeeNo,
        CancellationToken cancellationToken = default)
    {
        return GetProfileByEmployeeNoAsync(employeeNo, cancellationToken);
    }

    private async Task<CloudOidcUserProfile?> GetProfileByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var row = await (from user in dbContext.Users.AsNoTracking()
                         join employee in dbContext.Employees.AsNoTracking()
                             on user.Id equals employee.Id
                         where user.Id == userId
                         select new ProfileRow(
                             user.Id,
                             employee.EmployeeNo,
                             employee.RealName,
                             user.IsEnabled,
                             employee.IsActive,
                             employee.RowVersion))
            .FirstOrDefaultAsync(cancellationToken);

        return row?.ToProfile();
    }

    private async Task<CloudOidcUserProfile?> GetProfileByEmployeeNoAsync(
        string employeeNo,
        CancellationToken cancellationToken)
    {
        var row = await (from user in dbContext.Users.AsNoTracking()
                         join employee in dbContext.Employees.AsNoTracking()
                             on user.Id equals employee.Id
                         where employee.EmployeeNo == employeeNo
                         select new ProfileRow(
                             user.Id,
                             employee.EmployeeNo,
                             employee.RealName,
                             user.IsEnabled,
                             employee.IsActive,
                             employee.RowVersion))
            .FirstOrDefaultAsync(cancellationToken);

        return row?.ToProfile();
    }

    private sealed record ProfileRow(
        Guid UserId,
        string EmployeeNo,
        string RealName,
        bool AccountEnabled,
        bool EmployeeActive,
        uint EmployeeRowVersion)
    {
        public CloudOidcUserProfile ToProfile()
        {
            return new CloudOidcUserProfile(
                UserId,
                EmployeeNo,
                RealName,
                AccountEnabled,
                EmployeeActive,
                null,
                CloudIdentityStatusVersions.Create(UserId, AccountEnabled, EmployeeActive, EmployeeRowVersion));
        }
    }
}
