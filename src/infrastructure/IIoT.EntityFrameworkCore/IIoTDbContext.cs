using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.Capacities;
using IIoT.Core.Production.Aggregates.DeviceLogs;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.PassStations;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.EntityFrameworkCore.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore;

public class IIoTDbContext(DbContextOptions<IIoTDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<MfgProcess> MfgProcesses => Set<MfgProcess>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Recipe> Recipes => Set<Recipe>();

    // === 过站数据（按工序分表，每新增工序加一行）===
    public DbSet<PassDataInjection> PassDataInjection => Set<PassDataInjection>();

    // === 产能（半小时明细，唯一数据源）===
    public DbSet<HourlyCapacity> HourlyCapacities => Set<HourlyCapacity>();

    // === 设备日志 ===
    public DbSet<DeviceLog> DeviceLogs => Set<DeviceLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IIoTDbContext).Assembly);
    }
}