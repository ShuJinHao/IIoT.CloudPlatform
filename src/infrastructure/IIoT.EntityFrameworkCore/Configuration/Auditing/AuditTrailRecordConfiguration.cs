using IIoT.EntityFrameworkCore.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.EntityFrameworkCore.Configuration.Auditing;

internal sealed class AuditTrailRecordConfiguration : IEntityTypeConfiguration<AuditTrailRecord>
{
    public void Configure(EntityTypeBuilder<AuditTrailRecord> builder)
    {
        builder.ToTable("audit_trails");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ActorEmployeeNo)
            .HasMaxLength(64);

        builder.Property(x => x.OperationType)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.TargetType)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.TargetIdOrKey)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.ExecutedAtUtc)
            .IsRequired();

        builder.Property(x => x.Summary)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(x => x.FailureReason)
            .HasMaxLength(512);

        builder.HasIndex(x => x.ExecutedAtUtc);
        builder.HasIndex(x => x.ActorUserId);
        builder.HasIndex(x => new { x.OperationType, x.TargetType });
    }
}
