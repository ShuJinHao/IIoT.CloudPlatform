using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IIoT.Core.Production.Aggregates.PassStations;

namespace IIoT.EntityFrameworkCore.Configuration.Production;

public class PassDataInjectionConfiguration : IEntityTypeConfiguration<PassDataInjection>
{
    public void Configure(EntityTypeBuilder<PassDataInjection> builder)
    {
        builder.ToTable("pass_data_injection");

        builder.HasKey(p => new { p.Id, p.CompletedTime });
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.DeviceId)
            .IsRequired()
            .HasColumnName("device_id");

        builder.Property(p => p.MacAddress)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("mac_address");

        builder.Property(p => p.ClientCode)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("client_code");

        builder.Property(p => p.CellResult)
            .IsRequired()
            .HasMaxLength(10)
            .HasColumnName("cell_result");

        builder.Property(p => p.CompletedTime)
            .IsRequired()
            .HasColumnName("completed_time");

        builder.Property(p => p.ReceivedAt)
            .IsRequired()
            .HasColumnName("received_at");

        builder.Property(p => p.Barcode)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("barcode");

        builder.Property(p => p.PreInjectionTime)
            .IsRequired()
            .HasColumnName("pre_injection_time");

        builder.Property(p => p.PreInjectionWeight)
            .IsRequired()
            .HasColumnType("numeric(10,4)")
            .HasColumnName("pre_injection_weight");

        builder.Property(p => p.PostInjectionTime)
            .IsRequired()
            .HasColumnName("post_injection_time");

        builder.Property(p => p.PostInjectionWeight)
            .IsRequired()
            .HasColumnType("numeric(10,4)")
            .HasColumnName("post_injection_weight");

        builder.Property(p => p.InjectionVolume)
            .IsRequired()
            .HasColumnType("numeric(10,4)")
            .HasColumnName("injection_volume");

        builder.HasIndex(p => p.DeviceId)
            .HasDatabaseName("ix_pass_data_injection_device_id");

        builder.HasIndex(p => new { p.MacAddress, p.ClientCode, p.CompletedTime })
            .HasDatabaseName("ix_pass_data_injection_mac_client_time");

        builder.HasIndex(p => p.Barcode)
            .HasDatabaseName("ix_pass_data_injection_barcode");

        builder.HasIndex(p => new { p.DeviceId, p.CompletedTime })
            .HasDatabaseName("ix_pass_data_injection_device_time");
    }
}