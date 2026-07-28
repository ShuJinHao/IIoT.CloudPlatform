using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace IIoT.EntityFrameworkCore.Identity;

public sealed class RolePolicyService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager) : IRolePolicyService
{
    public async Task<IList<string>> GetAllRolesAsync()
    {
        return await roleManager.Roles.Select(r => r.Name!).ToListAsync();
    }

    public Task<bool> RoleExistsAsync(string roleName)
    {
        return roleManager.RoleExistsAsync((roleName ?? string.Empty).Trim());
    }

    public async Task<Result> CreateRoleAsync(string roleName)
    {
        var normalizedRoleName = (roleName ?? string.Empty).Trim();
        if (SystemRoles.IsAdminLike(roleName))
        {
            return Result.Failure("禁止定义、覆盖或修改 Admin 角色。");
        }

        if (await roleManager.RoleExistsAsync(normalizedRoleName))
        {
            return Result.Failure("角色已存在，DefineRolePolicy 只允许创建新角色。");
        }

        var result = await roleManager.CreateAsync(new IdentityRole<Guid>(normalizedRoleName));
        return result.ToResult();
    }

    public async Task<Result> DeleteRoleAsync(string roleName)
    {
        var role = await roleManager.FindByNameAsync(roleName);
        if (role == null) return Result.Success();

        var result = await roleManager.DeleteAsync(role);
        return result.ToResult();
    }

    public async Task<Result> RemoveRoleFromUserAsync(string employeeNo, string roleName)
    {
        var user = await userManager.FindByNameAsync(employeeNo);
        if (user == null) return Result.Failure("\u7528\u6237\u4E0D\u5B58\u5728");

        var result = await userManager.RemoveFromRoleAsync(user, roleName);
        return result.ToResult();
    }

    public async Task<List<string>?> GetRolePermissionsAsync(string roleName)
    {
        var role = await roleManager.FindByNameAsync((roleName ?? string.Empty).Trim());
        if (role == null) return null;

        var claims = await roleManager.GetClaimsAsync(role);
        return claims
            .Where(c => c.Type == IIoTClaimTypes.Permission)
            .Select(c => c.Value)
            .ToList();
    }

    public async Task<Result<bool>> UpdateRolePermissionsAsync(string roleName, List<string> permissions)
    {
        var normalizedRoleName = (roleName ?? string.Empty).Trim();
        if (SystemRoles.IsAdminLike(roleName))
        {
            return Result.Failure("禁止定义、覆盖或修改 Admin 角色。");
        }

        var validation = CloudPermissionCatalog.NormalizeForTargetRole(
            normalizedRoleName,
            permissions);
        if (!validation.IsValid)
        {
            return Result.Failure("权限集合包含未知权限或 RoleAdmin 不可分配权限，未执行任何修改。");
        }

        var effectivePermissions = validation.Permissions.ToList();

        var role = await roleManager.FindByNameAsync(normalizedRoleName);
        if (role == null) return Result.Failure("\u89D2\u8272\u4E0D\u5B58\u5728");

        var claims = await roleManager.GetClaimsAsync(role);
        var existingPermissions = claims
            .Where(c => c.Type == IIoTClaimTypes.Permission)
            .ToList();
        var desiredPermissions = effectivePermissions.ToDictionary(
            permission => permission,
            permission => permission,
            StringComparer.OrdinalIgnoreCase);
        var retainedPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var claim in existingPermissions)
        {
            var shouldRetain =
                desiredPermissions.TryGetValue(claim.Value.Trim(), out var canonicalPermission)
                && string.Equals(claim.Value, canonicalPermission, StringComparison.Ordinal)
                && retainedPermissions.Add(canonicalPermission);
            if (shouldRetain)
            {
                continue;
            }

            var removeResult = await roleManager.RemoveClaimAsync(role, claim);
            if (!removeResult.Succeeded)
                return Result.Failure(removeResult.Errors.Select(error => error.Description).ToArray());
        }

        foreach (var permission in effectivePermissions.Where(permission => !retainedPermissions.Contains(permission)))
        {
            var addResult = await roleManager.AddClaimAsync(
                role,
                new Claim(IIoTClaimTypes.Permission, permission));
            if (!addResult.Succeeded)
                return Result.Failure(addResult.Errors.Select(error => error.Description).ToArray());
        }

        return Result.Success(true);
    }

    public async Task<Result<bool>> UpdateUserPersonalPermissionsAsync(Guid userId, List<string> permissions)
    {
        var validation = CloudPermissionCatalog.Normalize(permissions);
        if (!validation.IsValid)
        {
            return Result.Failure("权限集合包含未知或空白权限，未执行任何修改。");
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user == null) return Result.Failure("\u7528\u6237\u4E0D\u5B58\u5728");

        var claims = await userManager.GetClaimsAsync(user);
        var existingPermissions = claims
            .Where(c => c.Type == IIoTClaimTypes.Permission)
            .ToList();
        var desiredPermissions = validation.Permissions.ToDictionary(
            permission => permission,
            permission => permission,
            StringComparer.OrdinalIgnoreCase);
        var retainedPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var claim in existingPermissions)
        {
            var shouldRetain =
                desiredPermissions.TryGetValue(claim.Value.Trim(), out var canonicalPermission)
                && string.Equals(claim.Value, canonicalPermission, StringComparison.Ordinal)
                && retainedPermissions.Add(canonicalPermission);
            if (shouldRetain)
            {
                continue;
            }

            var removeResult = await userManager.RemoveClaimAsync(user, claim);
            if (!removeResult.Succeeded)
                return Result.Failure(removeResult.Errors.Select(error => error.Description).ToArray());
        }

        foreach (var permission in validation.Permissions.Where(permission => !retainedPermissions.Contains(permission)))
        {
            var addResult = await userManager.AddClaimAsync(
                user,
                new Claim(IIoTClaimTypes.Permission, permission));
            if (!addResult.Succeeded)
                return Result.Failure(addResult.Errors.Select(error => error.Description).ToArray());
        }

        return Result.Success(true);
    }

    public async Task<List<string>> GetUserPersonalPermissionsAsync(Guid userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user == null) return [];

        var claims = await userManager.GetClaimsAsync(user);
        return claims
            .Where(c => c.Type == IIoTClaimTypes.Permission)
            .Select(c => c.Value)
            .ToList();
    }
}
