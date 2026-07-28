using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace IIoT.MigrationWorkApp.SeedData;

public static class SystemInitData
{
    public static async Task SeedAsync(
        IIoTDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ValidateRolePermissionTemplates();

        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await SeedCoreAsync(
                    dbContext,
                    userManager,
                    roleManager,
                    configuration,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                Console.WriteLine("✅ 系统身份角色模板和管理员播种事务提交成功。");
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    private static async Task SeedCoreAsync(
        IIoTDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        // 1. 确保超级管理员和岗位角色模板存在
        var adminRoleName = SystemRoles.Admin;
        await EnsureRoleAsync(roleManager, adminRoleName);
        await EnsureRolePermissionTemplatesAsync(roleManager);

        // 2. 已存在管理员账号时直接跳过，不再要求提供种子凭据
        var existingAdmins = await userManager.GetUsersInRoleAsync(adminRoleName);
        var resetPasswordRequested = SeedAdminOptions.IsPasswordResetRequested(configuration);
        if (existingAdmins.Count > 0 && !resetPasswordRequested)
        {
            Console.WriteLine($"ℹ️ 检测到已存在的管理员账号，跳过播种逻辑。");
            return;
        }

        // 3. 初始化目标账号参数
        var seedAdmin = SeedAdminOptions.Load(configuration);
        if (existingAdmins.Count > 0 && resetPasswordRequested)
        {
            Console.WriteLine($"ℹ️ 检测到管理员账号，按显式运维开关修复账号 [{seedAdmin.EmployeeNo}]。");
            await EnsureSeedAdminAccountAsync(
                dbContext,
                userManager,
                adminRoleName,
                seedAdmin,
                resetPassword: true,
                cancellationToken);
            return;
        }

        await EnsureSeedAdminAccountAsync(
            dbContext,
            userManager,
            adminRoleName,
            seedAdmin,
            resetPassword: false,
            cancellationToken);
    }

    private static void ValidateRolePermissionTemplates()
    {
        foreach (var (roleName, permissions) in SystemRolePermissionTemplates.Templates)
        {
            if (string.IsNullOrWhiteSpace(roleName)
                || SystemRoles.IsAdminLike(roleName))
            {
                throw new InvalidOperationException(
                    $"内置角色模板名称非法：[{roleName}]。");
            }

            var validation = CloudPermissionCatalog.NormalizeForTargetRole(
                roleName,
                permissions);
            if (!validation.IsValid
                || validation.Permissions.Count != permissions.Count)
            {
                throw new InvalidOperationException(
                    $"内置角色模板 [{roleName}] 包含未知、重复或不可分配权限。");
            }

            if (string.Equals(
                    roleName,
                    SystemRoles.DeviceAdmin,
                    StringComparison.OrdinalIgnoreCase)
                && permissions.Any(permission =>
                    SystemRolePermissionTemplates.DeviceAdminRetiredPermissions.Contains(
                        permission,
                        StringComparer.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "DeviceAdmin 内置模板不得重新携带设备注册或删除权限。");
            }
        }
    }

    private static async Task EnsureRolePermissionTemplatesAsync(
        RoleManager<IdentityRole<Guid>> roleManager)
    {
        foreach (var (roleName, permissions) in SystemRolePermissionTemplates.Templates)
        {
            var role = await EnsureRoleAsync(roleManager, roleName);
            var claims = await roleManager.GetClaimsAsync(role);
            var retiredClaims = SelectRetiredDeviceAdminPermissionClaims(
                roleName,
                claims);
            foreach (var retiredClaim in retiredClaims)
            {
                var removeResult = await roleManager.RemoveClaimAsync(role, retiredClaim);
                if (!removeResult.Succeeded)
                {
                    Console.WriteLine(
                        $"❌ 角色 [{roleName}] 旧高危权限 [{retiredClaim.Value}] 清理失败！");
                    foreach (var error in removeResult.Errors)
                    {
                        Console.WriteLine($"   - [{error.Code}]: {error.Description}");
                    }

                    throw new Exception($"角色 [{roleName}] 旧高危权限清理失败。");
                }
            }

            var existingPermissions = claims
                .Where(claim => claim.Type == IIoTClaimTypes.Permission)
                .Except(retiredClaims)
                .Select(claim => claim.Value.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var permission in permissions)
            {
                if (existingPermissions.Contains(permission))
                {
                    continue;
                }

                var addResult = await roleManager.AddClaimAsync(
                    role,
                    new Claim(IIoTClaimTypes.Permission, permission));
                if (!addResult.Succeeded)
                {
                    Console.WriteLine($"❌ 角色 [{roleName}] 权限 [{permission}] 播种失败！");
                    foreach (var error in addResult.Errors)
                    {
                        Console.WriteLine($"   - [{error.Code}]: {error.Description}");
                    }

                    throw new Exception($"角色 [{roleName}] 权限播种失败。");
                }
            }
        }
    }

    internal static IReadOnlyList<Claim> SelectRetiredDeviceAdminPermissionClaims(
        string roleName,
        IEnumerable<Claim> claims)
    {
        if (!string.Equals(
                roleName.Trim(),
                SystemRoles.DeviceAdmin,
                StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var retiredPermissions = SystemRolePermissionTemplates.DeviceAdminRetiredPermissions
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return claims
            .Where(claim =>
                claim.Type == IIoTClaimTypes.Permission
                && retiredPermissions.Contains(claim.Value.Trim()))
            .ToArray();
    }

    private static async Task<IdentityRole<Guid>> EnsureRoleAsync(
        RoleManager<IdentityRole<Guid>> roleManager,
        string roleName)
    {
        var role = await roleManager.FindByNameAsync(roleName);
        if (role is not null)
        {
            return role;
        }

        role = new IdentityRole<Guid>(roleName);
        var createResult = await roleManager.CreateAsync(role);
        if (!createResult.Succeeded)
        {
            Console.WriteLine($"❌ 角色 [{roleName}] 创建失败！");
            foreach (var error in createResult.Errors)
            {
                Console.WriteLine($"   - [{error.Code}]: {error.Description}");
            }

            throw new Exception($"角色 [{roleName}] 创建失败。");
        }

        Console.WriteLine($"✅ 角色 [{roleName}] 创建成功！");
        return role;
    }

    private static async Task EnsureSeedAdminAccountAsync(
        IIoTDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        string adminRoleName,
        SeedAdminOptions seedAdmin,
        bool resetPassword,
        CancellationToken cancellationToken)
    {
        var targetPassword = seedAdmin.RequirePassword();
        var identityUser = await userManager.FindByNameAsync(seedAdmin.EmployeeNo);
        var createdUser = false;

        if (identityUser is null)
        {
            identityUser = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = seedAdmin.EmployeeNo,
                IsEnabled = true
            };

            var createResult = await userManager.CreateAsync(identityUser, targetPassword);
            if (!createResult.Succeeded)
            {
                Console.WriteLine($"❌ 账号 [{seedAdmin.EmployeeNo}] 创建失败！");
                foreach (var error in createResult.Errors)
                {
                    Console.WriteLine($"   - [{error.Code}]: {error.Description}");
                }

                throw new Exception("Identity 账号创建失败，事务终止！");
            }

            createdUser = true;
        }
        else
        {
            if (!identityUser.IsEnabled)
            {
                identityUser.IsEnabled = true;
                var updateResult = await userManager.UpdateAsync(identityUser);
                if (!updateResult.Succeeded)
                {
                    Console.WriteLine($"❌ 账号 [{seedAdmin.EmployeeNo}] 启用失败！");
                    foreach (var error in updateResult.Errors)
                    {
                        Console.WriteLine($"   - [{error.Code}]: {error.Description}");
                    }

                    throw new Exception("Identity 账号启用失败，事务终止！");
                }
            }

            if (resetPassword)
            {
                await ResetPasswordAsync(
                    userManager,
                    identityUser,
                    targetPassword,
                    seedAdmin.EmployeeNo);
            }
        }

        if (!await userManager.IsInRoleAsync(identityUser, adminRoleName))
        {
            var addRoleResult = await userManager.AddToRoleAsync(identityUser, adminRoleName);
            if (!addRoleResult.Succeeded)
            {
                Console.WriteLine($"❌ 账号 [{seedAdmin.EmployeeNo}] 授予 Admin 角色失败！");
                foreach (var error in addRoleResult.Errors)
                {
                    Console.WriteLine($"   - [{error.Code}]: {error.Description}");
                }

                throw new Exception("Admin 角色授予失败，事务终止！");
            }
        }

        var employee = await dbContext.Employees
            .SingleOrDefaultAsync(x => x.Id == identityUser.Id, cancellationToken);

        if (employee is null)
        {
            employee = new Employee(identityUser.Id, seedAdmin.EmployeeNo, seedAdmin.RealName);
            dbContext.Employees.Add(employee);
        }
        else
        {
            employee.Rename(seedAdmin.EmployeeNo, seedAdmin.RealName);
            employee.Activate();
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (createdUser)
        {
            Console.WriteLine($"✅ 账号 [{seedAdmin.EmployeeNo}] 及员工业务数据已准备播种。");
        }
        else if (resetPassword)
        {
            Console.WriteLine($"✅ 账号 [{seedAdmin.EmployeeNo}] 已准备按显式运维开关修复。");
        }
        else
        {
            Console.WriteLine($"✅ 账号 [{seedAdmin.EmployeeNo}] 与员工状态已核对。");
        }
    }

    private static async Task ResetPasswordAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationUser identityUser,
        string targetPassword,
        string employeeNo)
    {
        if (!string.IsNullOrWhiteSpace(identityUser.PasswordHash))
        {
            var removePasswordResult = await userManager.RemovePasswordAsync(identityUser);
            if (!removePasswordResult.Succeeded)
            {
                Console.WriteLine($"❌ 账号 [{employeeNo}] 移除旧密码失败！");
                foreach (var error in removePasswordResult.Errors)
                {
                    Console.WriteLine($"   - [{error.Code}]: {error.Description}");
                }

                throw new Exception("Identity 旧密码移除失败，事务终止！");
            }
        }

        var addPasswordResult = await userManager.AddPasswordAsync(identityUser, targetPassword);
        if (!addPasswordResult.Succeeded)
        {
            Console.WriteLine($"❌ 账号 [{employeeNo}] 设置新密码失败！");
            foreach (var error in addPasswordResult.Errors)
            {
                Console.WriteLine($"   - [{error.Code}]: {error.Description}");
            }

            throw new Exception("Identity 新密码设置失败，事务终止！");
        }
    }
}
