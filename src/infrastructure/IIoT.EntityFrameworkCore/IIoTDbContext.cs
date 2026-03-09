using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.EntityFrameworkCore.Identity;

namespace IIoT.EntityFrameworkCore;

// 🌟 核心改造：继承 IdentityDbContext，并指定 ApplicationUser, IdentityRole<Guid>, Guid 主键
public class IIoTDbContext(DbContextOptions<IIoTDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<MfgProcess> MfgProcesses => Set<MfgProcess>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Recipe> Recipes => Set<Recipe>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 🌟 关键：必须先调用 base，否则 Identity 内置的 AspNetUsers 等表无法生成
        base.OnModelCreating(modelBuilder);

        // 丝滑加载领域配置
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IIoTDbContext).Assembly);
    }
}