using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IIoT.Core.Employee.Aggregates.MfgProcesses;

namespace IIoT.EntityFrameworkCore.Configuration.Employee;

/// <summary>
/// 制造工序实体的 EF Core 数据库映射配置
/// </summary>
public class MfgProcessConfiguration : IEntityTypeConfiguration<MfgProcess>
{
    public void Configure(EntityTypeBuilder<MfgProcess> builder)
    {
        // 配置表名
        builder.ToTable("mfg_processes");

        // 配置主键
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        // 配置属性
        builder.Property(p => p.ProcessCode)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("process_code");

        builder.Property(p => p.ProcessName)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("process_name");

        // 工序编码全局唯一
        builder.HasIndex(p => p.ProcessCode)
            .IsUnique()
            .HasDatabaseName("ix_mfg_processes_process_code");
    }
}