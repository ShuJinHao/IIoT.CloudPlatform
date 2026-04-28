using EmployeeEntity = IIoT.Core.Employees.Aggregates.Employees.Employee;
using IIoT.EntityFrameworkCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.EntityFrameworkCore.Configuration.Employee;

/// <summary>
/// 员工（操作员）实体的 EF Core 映射配置。
/// </summary>
public class EmployeeConfiguration : IEntityTypeConfiguration<EmployeeEntity>
{
    public void Configure(EntityTypeBuilder<EmployeeEntity> builder)
    {
        builder.ToTable("employees");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<EmployeeEntity>(e => e.Id)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(e => e.EmployeeNo)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("employee_no");

        builder.Property(e => e.RealName)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("real_name");

        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasColumnName("is_active");

        builder.Property(e => e.RowVersion)
            .HasColumnName("xmin")
            .IsRowVersion();

        builder.HasIndex(e => e.EmployeeNo)
            .IsUnique()
            .HasDatabaseName("ix_employees_employee_no");

        builder.HasMany(e => e.DeviceAccesses)
            .WithOne(eda => eda.Employee)
            .HasForeignKey(eda => eda.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
