using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IIoT.Core.Employee.Aggregates.Employees;

namespace IIoT.EntityFrameworkCore.Configuration.Employee;

/// <summary>
/// 员工与工序权限中间表的 EF Core 数据库映射配置
/// </summary>
public class EmployeeProcessAccessConfiguration : IEntityTypeConfiguration<EmployeeProcessAccess>
{
    public void Configure(EntityTypeBuilder<EmployeeProcessAccess> builder)
    {
        // 配置表名
        builder.ToTable("employee_process_accesses");

        // 🌟 核心杀招：配置基于 Guid 的联合主键
        builder.HasKey(epa => new { epa.EmployeeId, epa.ProcessId });

        // 配置外键列名
        builder.Property(epa => epa.EmployeeId)
            .HasColumnName("employee_id");

        builder.Property(epa => epa.ProcessId)
            .HasColumnName("process_id");

        // 索引：加速按工序反查有哪些员工
        builder.HasIndex(epa => epa.ProcessId)
            .HasDatabaseName("ix_employee_process_accesses_process_id");
    }
}