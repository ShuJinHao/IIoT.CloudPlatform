using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace IIoT.EntityFrameworkCore.Identity;

public class IdentityService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager) : IIdentityService
{
    public async Task<Result> CreateUserAsync(Guid id, string employeeNo, string password)
    {
        var user = new ApplicationUser { Id = id, UserName = employeeNo };
        var result = await userManager.CreateAsync(user, password);

        if (!result.Succeeded)
            return Result.Failure(result.Errors.Select(e => e.Description).ToArray());

        return Result.Success();
    }

    public async Task<Result> CreateRoleAsync(string roleName)
    {
        if (await roleManager.RoleExistsAsync(roleName))
            return Result.Failure("角色已存在");

        var result = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
        if (!result.Succeeded)
            return Result.Failure(result.Errors.Select(e => e.Description).ToArray());

        return Result.Success();
    }

    public async Task<Result<bool>> CheckPasswordAsync(string employeeNo, string password)
    {
        var user = await userManager.FindByNameAsync(employeeNo);
        if (user == null) return Result.Success(false);

        var isValid = await userManager.CheckPasswordAsync(user, password);
        return Result.Success(isValid);
    }

    public async Task<IList<string>> GetRolesAsync(string employeeNo)
    {
        var user = await userManager.FindByNameAsync(employeeNo);
        if (user == null) return new List<string>();

        return await userManager.GetRolesAsync(user);
    }

    public async Task<Guid?> GetUserIdByEmployeeNoAsync(string employeeNo)
    {
        var user = await userManager.FindByNameAsync(employeeNo);
        return user?.Id;
    }

    public async Task<Result<bool>> UpdateRolePermissionsAsync(string roleName, List<string> permissions)
    {
        var role = await roleManager.FindByNameAsync(roleName);
        if (role == null) return Result.Failure("角色不存在"); // 🌟 修正为 Result.Failure

        var existingClaims = await roleManager.GetClaimsAsync(role);
        var existingPermissions = existingClaims.Where(c => c.Type == "Permission").ToList();

        foreach (var claim in existingPermissions)
        {
            await roleManager.RemoveClaimAsync(role, claim);
        }

        foreach (var permission in permissions)
        {
            await roleManager.AddClaimAsync(role, new Claim("Permission", permission));
        }

        return Result.Success(true); // 🌟 修正为 Result.Success(true)
    }

    public async Task<Result<bool>> AssignRoleToUserAsync(string employeeNo, string roleName)
    {
        var user = await userManager.FindByNameAsync(employeeNo);
        if (user == null) return Result.Failure("用户不存在"); // 🌟 修正

        if (!await roleManager.RoleExistsAsync(roleName)) return Result.Failure("角色不存在"); // 🌟 修正

        if (!await userManager.IsInRoleAsync(user, roleName))
        {
            var result = await userManager.AddToRoleAsync(user, roleName);
            // Result.Failure 接收 params object[]，这里直接把错误信息转成数组传入
            if (!result.Succeeded) return Result.Failure(result.Errors.Select(e => e.Description).ToArray());
        }

        return Result.Success(true);
    }
}