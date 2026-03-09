using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IIoT.Core.Employee.Aggregates.Employees;

namespace IIoT.EntityFrameworkCore.Configuration.Employee;

/// <summary>
/// 员工(操作员)实体的 EF Core 数据库映射配置
/// </summary>
public class EmployeeConfiguration : IEntityTypeConfiguration<Core.Employee.Aggregates.Employees.Employee>
{
    public void Configure(EntityTypeBuilder<Core.Employee.Aggregates.Employees.Employee> builder)
    {
        // 1. 配置表名 (严格使用小写复数下划线命名法)
        builder.ToTable("employees");

        // 2. 配置主键 (Guid 映射为 PostgreSQL 的 uuid)
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        // 3. 配置基本属性
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

        // 4. 配置索引：工号在全厂必须唯一，用于登录
        builder.HasIndex(e => e.EmployeeNo)
            .IsUnique()
            .HasDatabaseName("ix_employees_employee_no");

        // 5. 配置一对多导航属性 (EF Core 会自动绑定后端的私有集合 _processAccesses)
        builder.HasMany(e => e.ProcessAccesses)
            .WithOne(epa => epa.Employee)
            .HasForeignKey(epa => epa.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade); // 级联删除：员工被删，权限映射一并清空
    }
}