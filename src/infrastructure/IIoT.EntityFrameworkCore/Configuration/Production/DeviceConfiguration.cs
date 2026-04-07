using IIoT.Core.Production.Aggregates.Devices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.EntityFrameworkCore.Configuration.Production;

public class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("devices");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id");

        builder.Property(d => d.DeviceName)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("device_name");

        // 值对象 ClientInstanceId → 两列(mac_address, client_code)
        builder.ComplexProperty(d => d.Instance, instance =>
        {
            instance.Property(i => i.MacAddress)
                .IsRequired()
                .HasMaxLength(50)
                .HasColumnName("mac_address");

            instance.Property(i => i.ClientCode)
                .IsRequired()
                .HasMaxLength(50)
                .HasColumnName("client_code");
        });

        builder.Property(d => d.ProcessId)
            .IsRequired()
            .HasColumnName("process_id");

        builder.Property(d => d.IsActive)
            .IsRequired()
            .HasColumnName("is_active");

        // 联合唯一:同一宿主机上同一 ClientCode 的实例只有一个
        builder.HasIndex(d => new { d.Instance.MacAddress, d.Instance.ClientCode })
            .IsUnique()
            .HasDatabaseName("ix_devices_mac_address_client_code");

        builder.HasIndex(d => d.ProcessId)
            .HasDatabaseName("ix_devices_process_id");
    }
}