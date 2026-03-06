using Microsoft.EntityFrameworkCore;

namespace IIoT.Infrastructure.EntityFrameworkCore;

/// <summary>
/// 工业云平台全局数据库上下文
/// </summary>
public class IIoTDbContext : DbContext
{
    public IIoTDbContext(DbContextOptions<IIoTDbContext> options) : base(options)
    {
    }

    // 虽然推荐通过 Repository 操作，但 DbContext 依然需要暴露 DbSet 给 EF Core 使用
    public DbSet<Core.Employee.Aggregates.Employees.Employee> Employees => Set<Core.Employee.Aggregates.Employees.Employee>();

    public DbSet<Core.Employee.Aggregates.MfgProcesses.MfgProcess> MfgProcesses => Set<Core.Employee.Aggregates.MfgProcesses.MfgProcess>();
    public DbSet<Core.Production.Aggregates.Devices.Device> Devices => Set<Core.Production.Aggregates.Devices.Device>();
    public DbSet<Core.Production.Aggregates.Recipes.Recipe> Recipes => Set<Core.Production.Aggregates.Recipes.Recipe>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 🌟 神级代码：自动扫描当前程序集，把咱们写在 Configuration 文件夹里的配置全加载进来
        // 再也不用手动一个个写 builder.ApplyConfiguration() 了！
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IIoTDbContext).Assembly);
    }
}