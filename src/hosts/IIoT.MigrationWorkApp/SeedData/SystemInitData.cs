using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Identity;
using Microsoft.AspNetCore.Identity;

namespace IIoT.MigrationWorkApp.SeedData;

public static class SystemInitData
{
    public static async Task SeedAsync(
        IIoTDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager)
    {
        // 1. 确保超级管理员角色存在
        var adminRoleName = "Admin";
        if (!await roleManager.RoleExistsAsync(adminRoleName))
        {
            await roleManager.CreateAsync(new IdentityRole<Guid>(adminRoleName));
        }

        // 2. 初始化目标账号
        var targetEmployeeNo = "101650";
        var targetPassword = "ljh123456";
        var realName = "系统管理员"; // 可根据实际情况修改

        // 3. 检查账号是否已存在，防止重复播种报错
        var existingUser = await userManager.FindByNameAsync(targetEmployeeNo);
        if (existingUser == null)
        {
            // 🌟 核心：生成全局唯一的“灵魂契约 ID”
            var sharedId = Guid.NewGuid();

            // 4. 创建底层身份认证账号 (保安)
            var identityUser = new ApplicationUser
            {
                Id = sharedId,
                UserName = targetEmployeeNo
            };

            // UserManager 会自动对 ljh123456 进行 Hash 加密并存入 AspNetUsers 表
            var result = await userManager.CreateAsync(identityUser, targetPassword);
            if (result.Succeeded)
            {
                // 给账号赋予 Admin 角色
                await userManager.AddToRoleAsync(identityUser, adminRoleName);

                // 5. 创建核心业务聚合根 (员工)，使用刚刚修改过的精简版构造函数
                var employee = new Employee(sharedId, targetEmployeeNo, realName);

                // 写入 employees 业务表
                dbContext.Employees.Add(employee);
                await dbContext.SaveChangesAsync();
            }
        }
    }
}