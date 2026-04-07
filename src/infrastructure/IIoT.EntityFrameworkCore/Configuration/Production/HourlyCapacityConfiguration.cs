using IIoT.Core.Production.Aggregates.Capacities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.EntityFrameworkCore.Configuration.Production;

public class HourlyCapacityConfiguration : IEntityTypeConfiguration<HourlyCapacity>
{
    public void Configure(EntityTypeBuilder<HourlyCapacity> builder)
    {
        builder.ToTable("hourly_capacity");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.DeviceId).IsRequired().HasColumnName("device_id");

        builder.Property(c => c.MacAddress)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("mac_address");

        builder.Property(c => c.ClientCode)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("client_code");

        builder.Property(c => c.Date).IsRequired().HasColumnName("date");
        builder.Property(c => c.ShiftCode).IsRequired().HasMaxLength(20).HasColumnName("shift_code");
        builder.Property(c => c.Hour).IsRequired().HasColumnName("hour");
        builder.Property(c => c.Minute).IsRequired().HasColumnName("minute");
        builder.Property(c => c.TimeLabel).IsRequired().HasMaxLength(20).HasColumnName("time_label");
        builder.Property(c => c.TotalCount).IsRequired().HasColumnName("total_count");
        builder.Property(c => c.OkCount).IsRequired().HasColumnName("ok_count");
        builder.Property(c => c.NgCount).IsRequired().HasColumnName("ng_count");

        builder.Property(c => c.PlcName)
            .HasMaxLength(100)
            .HasColumnName("plc_name")
            .IsRequired(false);

        builder.Property(c => c.ReportedAt).IsRequired().HasColumnName("reported_at");

        builder.HasIndex(c => new
        {
            c.DeviceId,
            c.Date,
            c.Hour,
            c.Minute,
            c.ShiftCode,
            c.PlcName
        })
            .IsUnique()
            .HasDatabaseName("ix_hourly_capacity_device_date_hour_minute_shift_plc");

        builder.HasIndex(c => new { c.MacAddress, c.ClientCode, c.Date })
            .HasDatabaseName("ix_hourly_capacity_mac_client_date");
    }
}