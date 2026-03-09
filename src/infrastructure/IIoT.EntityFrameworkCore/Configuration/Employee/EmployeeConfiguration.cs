using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IIoT.EntityFrameworkCore.Identity;

namespace IIoT.EntityFrameworkCore.Configuration.Employee;

/// <summary>
/// 员工(操作员)实体的 EF Core 数据库映射配置
/// </summary>
public class EmployeeConfiguration : IEntityTypeConfiguration<Core.Employee.Aggregates.Employees.Employee>
{
    public void Configure(EntityTypeBuilder<Core.Employee.Aggregates.Employees.Employee> builder)
    {
        builder.ToTable("employees");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        // 🌟 核心杀招：确立 IIdentity 为最高主宰！
        // 声明 Employee.Id 是指向 ApplicationUser.Id 的外键，并且配置级联删除！
        // 只要调用 IdentityService 删除账号，数据库会自动把 Employee 档案和名下的权限全删光。
        builder.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<Core.Employee.Aggregates.Employees.Employee>(e => e.Id)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(e => e.EmployeeNo)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("employee_no");

        builder.Property(e => e.RealName)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("real_name");

        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasColumnName("is_active");

        builder.HasIndex(e => e.EmployeeNo)
            .IsUnique()
            .HasDatabaseName("ix_employees_employee_no");

        // 配置一对多导航属性：工序管辖权
        builder.HasMany(e => e.ProcessAccesses)
            .WithOne(epa => epa.Employee)
            .HasForeignKey(epa => epa.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        // 配置一对多导航属性：具体设备管辖权
        builder.HasMany(e => e.DeviceAccesses)
            .WithOne(eda => eda.Employee)
            .HasForeignKey(eda => eda.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}