using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IIoT.Core.Production.Aggregates.Devices;

namespace IIoT.Infrastructure.EntityFrameworkCore.Configuration.Production;

/// <summary>
/// 物理设备终端的 EF Core 数据库映射配置
/// </summary>
public class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("devices");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id");

        builder.Property(d => d.DeviceCode)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("device_code");

        builder.Property(d => d.MacAddress)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("mac_address");

        builder.Property(d => d.ProcessId)
            .IsRequired()
            .HasColumnName("process_id");

        builder.Property(d => d.IsActive)
            .IsRequired()
            .HasColumnName("is_active");

        // 防伪标识：MAC 地址在全厂必须唯一
        builder.HasIndex(d => d.MacAddress)
            .IsUnique()
            .HasDatabaseName("ix_devices_mac_address");

        // 业务索引：经常需要根据工序(ProcessId)查找下面的所有设备
        builder.HasIndex(d => d.ProcessId)
            .HasDatabaseName("ix_devices_process_id");
    }
}