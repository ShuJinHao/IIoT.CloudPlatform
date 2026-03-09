using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace IIoT.EntityFrameworkCore.Identity;

/// <summary>
/// 身份认证服务实现 (保安科)
/// 优化重点：减少数据库交互、统一错误处理、确保操作原子性
/// </summary>
public class IdentityService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager) : IIdentityService
{
    private const string PermissionClaimType = "Permission"; // 🌟 抽取常量，防止硬编码写错

    #region 账号管理 (Account)

    public async Task<Result> CreateUserAsync(Guid id, string employeeNo, string password)
    {
        var user = new ApplicationUser { Id = id, UserName = employeeNo };
        var result = await userManager.CreateAsync(user, password);

        return ToResult(result); // 🌟 优化：使用统一转换方法
    }

    public async Task<Result<bool>> CheckPasswordAsync(string employeeNo, string password)
    {
        var user = await userManager.FindByNameAsync(employeeNo);
        if (user == null) return Result.Success(false);

        var isValid = await userManager.CheckPasswordAsync(user, password);
        return Result.Success(isValid);
    }

    public async Task<Guid?> GetUserIdByEmployeeNoAsync(string employeeNo)
    {
        var user = await userManager.FindByNameAsync(employeeNo);
        return user?.Id;
    }

    #endregion 账号管理 (Account)

    #region 角色与权限策略 (Security Policy)

    public async Task<Result> CreateRoleAsync(string roleName)
    {
        if (await roleManager.RoleExistsAsync(roleName))
            return Result.Failure("角色已存在");

        var result = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
        return ToResult(result);
    }

    public async Task<Result<bool>> UpdateRolePermissionsAsync(string roleName, List<string> permissions)
    {
        var role = await roleManager.FindByNameAsync(roleName);
        if (role == null) return Result.Failure("角色不存在");

        // 1. 获取现有权限 Claim
        var claims = await roleManager.GetClaimsAsync(role);
        var existingPermissions = claims.Where(c => c.Type == PermissionClaimType).ToList();

        // 🌟 优化逻辑：不要直接全部删掉再加。计算差集，只删除该删的，只添加新增的。
        // 这样可以极大地减少数据库操作次数，并减少数据库日志压力。
        var toRemove = existingPermissions.Where(c => !permissions.Contains(c.Value)).ToList();
        var toAdd = permissions.Where(p => !existingPermissions.Any(c => c.Value == p)).ToList();

        foreach (var claim in toRemove)
        {
            await roleManager.RemoveClaimAsync(role, claim);
        }

        foreach (var permission in toAdd)
        {
            await roleManager.AddClaimAsync(role, new Claim(PermissionClaimType, permission));
        }

        return Result.Success(true);
    }

    public async Task<Result<bool>> UpdateUserPersonalPermissionsAsync(Guid userId, List<string> permissions)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user == null) return Result.Failure("用户不存在");

        var claims = await userManager.GetClaimsAsync(user);
        var existingPermissions = claims.Where(c => c.Type == PermissionClaimType).ToList();

        // 🌟 同样使用差集优化逻辑
        var toRemove = existingPermissions.Where(c => !permissions.Contains(c.Value)).ToList();
        var toAdd = permissions.Where(p => !existingPermissions.Any(c => c.Value == p)).ToList();

        foreach (var claim in toRemove) await userManager.RemoveClaimAsync(user, claim);
        foreach (var p in toAdd) await userManager.AddClaimAsync(user, new Claim(PermissionClaimType, p));

        return Result.Success(true);
    }

    #endregion 角色与权限策略 (Security Policy)

    #region 关联操作 (Assignment)

    public async Task<Result<bool>> AssignRoleToUserAsync(string employeeNo, string roleName)
    {
        var user = await userManager.FindByNameAsync(employeeNo);
        if (user == null) return Result.Failure("用户不存在");

        if (!await roleManager.RoleExistsAsync(roleName))
            return Result.Failure("角色未定义");

        // 🌟 幂等性保护：如果已经在角色里，直接返回成功，不报异常
        if (await userManager.IsInRoleAsync(user, roleName))
            return Result.Success(true);

        var result = await userManager.AddToRoleAsync(user, roleName);
        return ToResult(result);
    }

    public async Task<IList<string>> GetRolesAsync(string employeeNo)
    {
        var user = await userManager.FindByNameAsync(employeeNo);
        return user == null ? [] : await userManager.GetRolesAsync(user);
    }

    #endregion 关联操作 (Assignment)

    #region 私有辅助方法

    /// <summary>
    /// 将 IdentityResult 转换为业务 Result
    /// </summary>
    private static Result ToResult(IdentityResult result)
    {
        return result.Succeeded
            ? Result.Success()
            : Result.Failure(result.Errors.Select(e => e.Description).ToArray());
    }

    #endregion 私有辅助方法
}