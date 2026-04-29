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

        builder.Property(d => d.Code)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("client_code");

        builder.Property(d => d.BootstrapSecretHash)
            .HasMaxLength(256)
            .HasColumnName("bootstrap_secret_hash");

        builder.Property(d => d.ProcessId)
            .IsRequired()
            .HasColumnName("process_id");

        builder.Property(d => d.RowVersion)
            .HasColumnName("xmin")
            .IsRowVersion();

        builder.HasIndex(d => d.Code)
            .IsUnique()
            .HasDatabaseName("ix_devices_client_code");

        builder.HasIndex(d => d.ProcessId)
            .HasDatabaseName("ix_devices_process_id");
    }
}
