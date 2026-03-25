using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IIoT.Core.Production.Aggregates.Capacities;

namespace IIoT.EntityFrameworkCore.Configuration.Production;

/// <summary>
/// 每日产能汇总的 EF Core 数据库映射配置
/// </summary>
public class DailyCapacityConfiguration : IEntityTypeConfiguration<DailyCapacity>
{
    public void Configure(EntityTypeBuilder<DailyCapacity> builder)
    {
        builder.ToTable("daily_capacity");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.DeviceId)
            .IsRequired()
            .HasColumnName("device_id");

        builder.Property(c => c.Date)
            .IsRequired()
            .HasColumnName("date");

        builder.Property(c => c.ShiftCode)
            .IsRequired()
            .HasMaxLength(20)
            .HasColumnName("shift_code");

        builder.Property(c => c.TotalCount)
            .IsRequired()
            .HasColumnName("total_count");

        builder.Property(c => c.OkCount)
            .IsRequired()
            .HasColumnName("ok_count");

        builder.Property(c => c.NgCount)
            .IsRequired()
            .HasColumnName("ng_count");

        builder.Property(c => c.ReportedAt)
            .IsRequired()
            .HasColumnName("reported_at");

        // 唯一约束：同一设备同一天同一班次不允许重复
        builder.HasIndex(c => new { c.DeviceId, c.Date, c.ShiftCode })
            .IsUnique()
            .HasDatabaseName("ix_daily_capacity_device_date_shift");
    }
}