using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IIoT.Core.Employee.Aggregates.Employees;

namespace IIoT.EntityFrameworkCore.Configuration.Employee;

/// <summary>
/// 员工与具体设备权限中间表的 EF Core 数据库映射配置
/// </summary>
public class EmployeeDeviceAccessConfiguration : IEntityTypeConfiguration<EmployeeDeviceAccess>
{
    public void Configure(EntityTypeBuilder<EmployeeDeviceAccess> builder)
    {
        // 1. 配置物理表名 (小写复数下划线)
        builder.ToTable("employee_device_accesses");

        // 2. 联合主键，同一员工不会重复绑定同一设备
        builder.HasKey(eda => new { eda.EmployeeId, eda.DeviceId });

        // 3. 配置外键列名
        builder.Property(eda => eda.EmployeeId)
            .HasColumnName("employee_id");

        builder.Property(eda => eda.DeviceId)
            .HasColumnName("device_id");

        // 4. 业务索引：加速按设备反查“这台机器能被哪些人操作”
        builder.HasIndex(eda => eda.DeviceId)
            .HasDatabaseName("ix_employee_device_accesses_device_id");
    }
}