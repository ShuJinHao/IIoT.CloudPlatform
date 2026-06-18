using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
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
        // 1. 确保超级管理员角色存在
        var adminRoleName = IIoT.Services.Contracts.Authorization.SystemRoles.Admin;
        if (!await roleManager.RoleExistsAsync(adminRoleName))
        {
            await roleManager.CreateAsync(new IdentityRole<Guid>(adminRoleName));
            Console.WriteLine($"✅ 角色 [{adminRoleName}] 创建成功！");
        }

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

    private static async Task EnsureSeedAdminAccountAsync(
        IIoTDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        string adminRoleName,
        SeedAdminOptions seedAdmin,
        bool resetPassword,
        CancellationToken cancellationToken)
    {
        var targetPassword = seedAdmin.RequirePassword();

        // 4. 获取执行策略(应对断网重试)
        var strategy = dbContext.Database.CreateExecutionStrategy();

        // 5. 将事务包裹在执行策略中
        await strategy.ExecuteAsync(async () =>
        {
            // 在策略内部合法开启强事务
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
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

                    // 6. 创建底层身份认证账号
                    var createResult = await userManager.CreateAsync(identityUser, targetPassword);

                    if (!createResult.Succeeded)
                    {
                        Console.WriteLine($"❌ 账号 [{seedAdmin.EmployeeNo}] 创建失败！详细死因如下：");
                        foreach (var error in createResult.Errors)
                        {
                            Console.WriteLine($"   - [{error.Code}]: {error.Description}");
                        }
                        // 直接抛出异常，精准触发下方的 catch 回滚
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
                        await ResetPasswordAsync(userManager, identityUser, targetPassword, seedAdmin.EmployeeNo);
                    }
                }

                // 7. 赋予 Admin 角色
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

                // 8. 创建或修复核心业务聚合根 (员工)
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

                // 9. 全部成功，提交事务
                await transaction.CommitAsync();
                if (createdUser)
                {
                    Console.WriteLine($"✅ 事务提交成功！账号 [{seedAdmin.EmployeeNo}] 及员工业务数据已完整播种！");
                }
                else if (resetPassword)
                {
                    Console.WriteLine($"✅ 账号 [{seedAdmin.EmployeeNo}] 密码、Admin 角色和员工状态已按显式运维开关修复。");
                }
                else
                {
                    Console.WriteLine($"✅ 账号 [{seedAdmin.EmployeeNo}] 已存在，Admin 角色和员工状态已确认。");
                }
            }
            catch (Exception ex)
            {
                // 任何异常都回滚
                await transaction.RollbackAsync();
                Console.WriteLine($"⛔ 发生致命错误，已触发事务回滚！所有脏数据已清除。错误信息: {ex.Message}");
                // 将异常继续抛出，让外层的重试机制知道失败了
                throw;
            }
        });
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
