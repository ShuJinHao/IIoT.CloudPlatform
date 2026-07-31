using IIoT.Core.Identity.Aggregates.IdentityAccounts;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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

    public async Task<IdentityAccountStateSnapshot?> GetStateSnapshotAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await userManager.Users
            .AsNoTracking()
            .Where(user => user.Id == id)
            .Select(user => new IdentityAccountStateSnapshot(
                user.IsEnabled,
                user.SecurityStamp))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Result<IdentityAccountCompareExchangeOutcome>> CompareExchangeStateAsync(
        Guid id,
        IdentityAccountStateSnapshot expected,
        bool isEnabled,
        string securityStamp,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentException.ThrowIfNullOrWhiteSpace(securityStamp);
        var nextConcurrencyStamp = Guid.NewGuid().ToString("N");
        var affected = await userManager.Users
            .Where(user =>
                user.Id == id
                && user.IsEnabled == expected.IsEnabled
                && user.SecurityStamp == expected.SecurityStamp)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.IsEnabled, isEnabled)
                    .SetProperty(user => user.SecurityStamp, securityStamp)
                    .SetProperty(user => user.ConcurrencyStamp, nextConcurrencyStamp),
                cancellationToken);

        return Result.Success(
            affected == 1
                ? IdentityAccountCompareExchangeOutcome.Applied
                : IdentityAccountCompareExchangeOutcome.Conflict);
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
        var normalizedRoleName = roleName?.Trim();
        if (SystemRoles.IsAdminLike(roleName))
        {
            return Result.Failure("管理员角色禁止通过该接口分配");
        }

        if (string.IsNullOrEmpty(normalizedRoleName))
        {
            return Result.Failure("角色未定义");
        }

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Result.Failure("用户不存在");
        }

        if (!await roleManager.RoleExistsAsync(normalizedRoleName))
        {
            return Result.Failure("角色未定义");
        }

        if (await userManager.IsInRoleAsync(user, normalizedRoleName))
        {
            return Result.Success(true);
        }

        var result = await userManager.AddToRoleAsync(user, normalizedRoleName);
        return result.Succeeded
            ? Result.Success(true)
            : Result.Failure(result.Errors.Select(e => e.Description).ToArray());
    }

    public async Task<Result<bool>> ReplaceAssignableRoleAsync(
        Guid id,
        string? roleName,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Result.Failure("用户不存在");
        }

        if (SystemRoles.IsAdminLike(roleName))
        {
            return Result.Failure("管理员角色禁止通过员工编辑维护");
        }

        string? canonicalRoleName = null;
        if (roleName is not null)
        {
            var normalizedRoleName = roleName.Trim();
            if (string.IsNullOrWhiteSpace(normalizedRoleName))
            {
                return Result.Failure("角色名称不能为空或纯空白");
            }

            var targetRole = await roleManager.FindByNameAsync(normalizedRoleName);
            canonicalRoleName = targetRole?.Name;
            if (string.IsNullOrWhiteSpace(canonicalRoleName))
            {
                return Result.Failure("角色未定义");
            }

            if (SystemRoles.IsAdminLike(canonicalRoleName))
            {
                return Result.Failure("管理员角色禁止通过员工编辑维护");
            }
        }

        var currentRoles = await userManager.GetRolesAsync(user);
        if (SystemRoles.ContainsAdminLike(currentRoles))
        {
            return Result.Failure("管理员角色禁止通过员工编辑维护");
        }

        var removableRoles = currentRoles
            .Where(role => !SystemRoles.IsAdminLike(role))
            .ToArray();
        var isNoChange = canonicalRoleName is null
            ? removableRoles.Length == 0
            : removableRoles.Length == 1
              && string.Equals(
                  removableRoles[0].Trim(),
                  canonicalRoleName,
                  StringComparison.OrdinalIgnoreCase);
        if (isNoChange)
        {
            return Result.Success(true);
        }

        if (removableRoles.Length > 0)
        {
            var removeResult = await userManager.RemoveFromRolesAsync(user, removableRoles);
            if (!removeResult.Succeeded)
            {
                return Result.Failure(removeResult.Errors.Select(e => e.Description).ToArray());
            }
        }

        if (canonicalRoleName is null)
        {
            return Result.Success(true);
        }

        var addResult = await userManager.AddToRoleAsync(user, canonicalRoleName);
        return addResult.Succeeded
            ? Result.Success(true)
            : Result.Failure(addResult.Errors.Select(e => e.Description).ToArray());
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
