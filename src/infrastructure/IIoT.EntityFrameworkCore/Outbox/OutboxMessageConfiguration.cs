using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.EntityFrameworkCore.Outbox;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(1024)
            .IsRequired();

        builder.Property(x => x.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.OccurredAtUtc)
            .HasColumnName("occurred_at_utc")
            .IsRequired();

        builder.Property(x => x.ProcessedAtUtc)
            .HasColumnName("processed_at_utc");

        builder.Property(x => x.LastAttemptedAtUtc)
            .HasColumnName("last_attempted_at_utc");

        builder.Property(x => x.AttemptCount)
            .HasColumnName("attempt_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.LastError)
            .HasColumnName("last_error")
            .HasColumnType("text");

        builder.Ignore(x => x.IsProcessed);

        builder.HasIndex(x => new { x.ProcessedAtUtc, x.OccurredAtUtc })
            .HasDatabaseName("ix_outbox_messages_dispatch");

        builder.HasIndex(x => x.LastAttemptedAtUtc)
            .HasDatabaseName("ix_outbox_messages_last_attempted");
    }
}
