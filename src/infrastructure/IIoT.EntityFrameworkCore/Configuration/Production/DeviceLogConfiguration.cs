using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IIoT.Core.Production.Aggregates.DeviceLogs;

namespace IIoT.EntityFrameworkCore.Configuration.Production;

/// <summary>
/// 设备运行日志的 EF Core 数据库映射配置
/// </summary>
public class DeviceLogConfiguration : IEntityTypeConfiguration<DeviceLog>
{
    public void Configure(EntityTypeBuilder<DeviceLog> builder)
    {
        builder.ToTable("device_logs");

        // TimescaleDB 超表要求：主键必须包含分区列 log_time
        builder.HasKey(l => new { l.Id, l.LogTime });
        builder.Property(l => l.Id).HasColumnName("id");

        builder.Property(l => l.DeviceId)
            .IsRequired()
            .HasColumnName("device_id");

        builder.Property(l => l.Level)
            .IsRequired()
            .HasMaxLength(10)
            .HasColumnName("level");

        builder.Property(l => l.Message)
            .IsRequired()
            .HasColumnName("message");

        builder.Property(l => l.LogTime)
            .IsRequired()
            .HasColumnName("log_time");

        builder.Property(l => l.ReceivedAt)
            .IsRequired()
            .HasColumnName("received_at");

        // 索引配置
        builder.HasIndex(l => new { l.DeviceId, l.LogTime })
            .HasDatabaseName("ix_device_logs_device_time");

        builder.HasIndex(l => l.Level)
            .HasDatabaseName("ix_device_logs_level");
    }
}