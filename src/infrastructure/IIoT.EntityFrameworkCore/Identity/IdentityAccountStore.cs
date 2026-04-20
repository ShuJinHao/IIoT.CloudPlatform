using IIoT.Core.Identity.Aggregates.IdentityAccounts;
using IIoT.Services.Contracts;
using IIoT.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;

namespace IIoT.EntityFrameworkCore.Identity;

public sealed class IdentityAccountStore(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager) : IIdentityAccountStore
{
    public async Task<Result<IdentityAccount>> CreateAsync(
        IdentityAccount account,
        CancellationToken cancellationToken = default)
    {
        var user = new ApplicationUser
        {
            Id = account.Id,
            UserName = account.EmployeeNo,
            IsEnabled = account.IsEnabled
        };

        var result = await userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            return Result.Failure(result.Errors.Select(e => e.Description).ToArray());
        }

        return Result.Success(account);
    }

    public async Task<IdentityAccount?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        return user is null ? null : Map(user);
    }

    public async Task<IdentityAccount?> GetByEmployeeNoAsync(
        string employeeNo,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByNameAsync(employeeNo);
        return user is null ? null : Map(user);
    }

    public async Task<Result<bool>> SetEnabledAsync(
        Guid id,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Result.Success(false);
        }

        if (user.IsEnabled == isEnabled)
        {
            return Result.Success(true);
        }

        user.IsEnabled = isEnabled;
        var result = await userManager.UpdateAsync(user);
        return result.Succeeded
            ? Result.Success(true)
            : Result.Failure(result.Errors.Select(e => e.Description).ToArray());
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Result.Success(true);
        }

        var result = await userManager.DeleteAsync(user);
        return result.Succeeded
            ? Result.Success(true)
            : Result.Failure(result.Errors.Select(e => e.Description).ToArray());
    }

    public async Task<Result<bool>> AssignRoleAsync(
        Guid id,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Result.Failure("用户不存在");
        }

        if (!await roleManager.RoleExistsAsync(roleName))
        {
            return Result.Failure("角色未定义");
        }

        if (await userManager.IsInRoleAsync(user, roleName))
        {
            return Result.Success(true);
        }

        var result = await userManager.AddToRoleAsync(user, roleName);
        return result.Succeeded
            ? Result.Success(true)
            : Result.Failure(result.Errors.Select(e => e.Description).ToArray());
    }

    public async Task<IList<string>> GetRolesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        return user is null ? [] : await userManager.GetRolesAsync(user);
    }

    private static IdentityAccount Map(ApplicationUser user)
    {
        var account = IdentityAccount.Create(user.Id, user.UserName!);
        if (!user.IsEnabled)
        {
            account.Disable();
        }

        return account;
    }
}
