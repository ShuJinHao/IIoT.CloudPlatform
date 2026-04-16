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
        var adminRoleName = "Admin";
        if (!await roleManager.RoleExistsAsync(adminRoleName))
        {
            await roleManager.CreateAsync(new IdentityRole<Guid>(adminRoleName));
            Console.WriteLine($"✅ 角色 [{adminRoleName}] 创建成功！");
        }

        // 2. 已存在管理员账号时直接跳过，不再要求提供种子凭据
        var existingAdmins = await userManager.GetUsersInRoleAsync(adminRoleName);
        if (existingAdmins.Count > 0)
        {
            Console.WriteLine($"ℹ️ 检测到已存在的管理员账号，跳过播种逻辑。");
            return;
        }

        // 3. 初始化目标账号参数
        var seedAdmin = SeedAdminOptions.Load(configuration);

        // 4. 检查目标账号是否已存在
        var existingUser = await userManager.FindByNameAsync(seedAdmin.EmployeeNo);
        if (existingUser != null)
        {
            Console.WriteLine($"ℹ️ 账号 [{seedAdmin.EmployeeNo}] 已存在，为其补充管理员角色。");

            if (!await userManager.IsInRoleAsync(existingUser, adminRoleName))
            {
                var addRoleResult = await userManager.AddToRoleAsync(existingUser, adminRoleName);
                if (!addRoleResult.Succeeded)
                {
                    throw new Exception("现有账号补充 Admin 角色失败，播种终止！");
                }
            }

            return;
        }

        var targetPassword = seedAdmin.RequirePassword();

        // 5. 获取执行策略(应对断网重试)
        var strategy = dbContext.Database.CreateExecutionStrategy();

        // 6. 将事务包裹在执行策略中
        await strategy.ExecuteAsync(async () =>
        {
            // 在策略内部合法开启强事务
            await using var transaction = await dbContext.Database.BeginTransactionAsync();

            try
            {
                var sharedId = Guid.NewGuid();

                var identityUser = new ApplicationUser
                {
                    Id = sharedId,
                    UserName = seedAdmin.EmployeeNo,
                    IsEnabled = true
                };

                // 7. 创建底层身份认证账号
                var result = await userManager.CreateAsync(identityUser, targetPassword);

                if (!result.Succeeded)
                {
                    Console.WriteLine($"❌ 账号 [{seedAdmin.EmployeeNo}] 创建失败！详细死因如下：");
                    foreach (var error in result.Errors)
                    {
                        Console.WriteLine($"   - [{error.Code}]: {error.Description}");
                    }
                    // 直接抛出异常，精准触发下方的 catch 回滚
                    throw new Exception("Identity 账号创建失败，事务终止！");
                }

                // 8. 赋予 Admin 角色
                await userManager.AddToRoleAsync(identityUser, adminRoleName);

                // 9. 创建核心业务聚合根 (员工)
                var employee = new Employee(sharedId, seedAdmin.EmployeeNo, seedAdmin.RealName);

                // 写入 employees 业务表
                dbContext.Employees.Add(employee);
                await dbContext.SaveChangesAsync();

                // 10. 全部成功，提交事务
                await transaction.CommitAsync();
                Console.WriteLine($"✅ 事务提交成功！账号 [{seedAdmin.EmployeeNo}] 及员工业务数据已完整播种！");
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
}

