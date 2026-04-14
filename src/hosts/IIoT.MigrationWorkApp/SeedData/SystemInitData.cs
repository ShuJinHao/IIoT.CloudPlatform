using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;

namespace IIoT.MigrationWorkApp.SeedData;

public static class SystemInitData
{
    public static async Task SeedAsync(
        IIoTDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        CancellationToken cancellationToken = default)
    {
        // 1. 确保超级管理员角色存在
        var adminRoleName = "Admin";
        if (!await roleManager.RoleExistsAsync(adminRoleName))
        {
            await roleManager.CreateAsync(new IdentityRole<Guid>(adminRoleName));
            Console.WriteLine($"✅ 角色 [{adminRoleName}] 创建成功！");
        }

        // 2. 初始化目标账号参数
        var targetEmployeeNo = "101650";
        var targetPassword = "Ljh123456!"; // 保持强密码规则
        var realName = "系统管理员";

        // 3. 检查账号是否已存在
        var existingUser = await userManager.FindByNameAsync(targetEmployeeNo);
        if (existingUser != null)
        {
            Console.WriteLine($"ℹ️ 账号 [{targetEmployeeNo}] 已存在，跳过播种逻辑。");
            return;
        }

        // 1. 获取执行策略(应对断网重试)
        var strategy = dbContext.Database.CreateExecutionStrategy();

        // 2. 将事务包裹在执行策略中
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
                    UserName = targetEmployeeNo,
                    IsEnabled = true
                };

                // 4. 创建底层身份认证账号
                var result = await userManager.CreateAsync(identityUser, targetPassword);

                if (!result.Succeeded)
                {
                    Console.WriteLine($"❌ 账号 [{targetEmployeeNo}] 创建失败！详细死因如下：");
                    foreach (var error in result.Errors)
                    {
                        Console.WriteLine($"   - [{error.Code}]: {error.Description}");
                    }
                    // 直接抛出异常，精准触发下方的 catch 回滚
                    throw new Exception("Identity 账号创建失败，事务终止！");
                }

                // 5. 赋予 Admin 角色
                await userManager.AddToRoleAsync(identityUser, adminRoleName);

                // 6. 创建核心业务聚合根 (员工)
                var employee = new Employee(sharedId, targetEmployeeNo, realName);

                // 写入 employees 业务表
                dbContext.Employees.Add(employee);
                await dbContext.SaveChangesAsync();

                // 3. 全部成功，提交事务
                await transaction.CommitAsync();
                Console.WriteLine($"✅ 事务提交成功！账号 [{targetEmployeeNo}] 及员工业务数据已完整播种！");
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
