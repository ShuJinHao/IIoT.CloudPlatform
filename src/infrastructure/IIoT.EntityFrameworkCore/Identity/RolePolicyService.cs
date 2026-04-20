using IIoT.Services.Contracts;
using IIoT.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace IIoT.EntityFrameworkCore.Identity;

public sealed class RolePolicyService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager) : IRolePolicyService
{
    private const string PermissionClaimType = "Permission";

    public async Task<IList<string>> GetAllRolesAsync()
    {
        return await roleManager.Roles.Select(r => r.Name!).ToListAsync();
    }

    public Task<bool> RoleExistsAsync(string roleName)
    {
        return roleManager.RoleExistsAsync(roleName);
    }

    public async Task<Result> CreateRoleAsync(string roleName)
    {
        if (await roleManager.RoleExistsAsync(roleName)) return Result.Success();

        var result = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
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
        var role = await roleManager.FindByNameAsync(roleName);
        if (role == null) return null;

        var claims = await roleManager.GetClaimsAsync(role);
        return claims
            .Where(c => c.Type == PermissionClaimType)
            .Select(c => c.Value)
            .ToList();
    }

    public async Task<Result<bool>> UpdateRolePermissionsAsync(string roleName, List<string> permissions)
    {
        var role = await roleManager.FindByNameAsync(roleName);
        if (role == null) return Result.Failure("\u89D2\u8272\u4E0D\u5B58\u5728");

        var claims = await roleManager.GetClaimsAsync(role);
        var existingPermissions = claims.Where(c => c.Type == PermissionClaimType).ToList();

        foreach (var claim in existingPermissions.Where(c => !permissions.Contains(c.Value)))
        {
            var removeResult = await roleManager.RemoveClaimAsync(role, claim);
            if (!removeResult.Succeeded)
                return Result.Failure(removeResult.Errors.Select(error => error.Description).ToArray());
        }

        foreach (var permission in permissions.Where(p => !existingPermissions.Any(c => c.Value == p)))
        {
            var addResult = await roleManager.AddClaimAsync(role, new Claim(PermissionClaimType, permission));
            if (!addResult.Succeeded)
                return Result.Failure(addResult.Errors.Select(error => error.Description).ToArray());
        }

        return Result.Success(true);
    }

    public async Task<Result<bool>> UpdateUserPersonalPermissionsAsync(Guid userId, List<string> permissions)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user == null) return Result.Failure("\u7528\u6237\u4E0D\u5B58\u5728");

        var claims = await userManager.GetClaimsAsync(user);
        var existingPermissions = claims.Where(c => c.Type == PermissionClaimType).ToList();

        foreach (var claim in existingPermissions.Where(c => !permissions.Contains(c.Value)))
        {
            var removeResult = await userManager.RemoveClaimAsync(user, claim);
            if (!removeResult.Succeeded)
                return Result.Failure(removeResult.Errors.Select(error => error.Description).ToArray());
        }

        foreach (var permission in permissions.Where(p => !existingPermissions.Any(c => c.Value == p)))
        {
            var addResult = await userManager.AddClaimAsync(user, new Claim(PermissionClaimType, permission));
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
            .Where(c => c.Type == PermissionClaimType)
            .Select(c => c.Value)
            .ToList();
    }
}
