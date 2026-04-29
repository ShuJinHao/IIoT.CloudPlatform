using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.EntityFrameworkCore.Uploads;

internal sealed class UploadReceiveRegistrationConfiguration
    : IEntityTypeConfiguration<UploadReceiveRegistration>
{
    public const string UniqueDeduplicationIndexName =
        "ux_upload_receive_registrations_device_message_deduplication";

    public void Configure(EntityTypeBuilder<UploadReceiveRegistration> builder)
    {
        builder.ToTable("upload_receive_registrations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DeviceId)
            .HasColumnName("device_id")
            .IsRequired();

        builder.Property(x => x.MessageType)
            .HasColumnName("message_type")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.RequestId)
            .HasColumnName("request_id")
            .HasMaxLength(128);

        builder.Property(x => x.DeduplicationKey)
            .HasColumnName("deduplication_key")
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(x => x.OutboxMessageId)
            .HasColumnName("outbox_message_id")
            .IsRequired();

        builder.Property(x => x.ReceivedAtUtc)
            .HasColumnName("received_at_utc")
            .IsRequired();

        builder.Property(x => x.LastSeenAtUtc)
            .HasColumnName("last_seen_at_utc")
            .IsRequired();

        builder.Property(x => x.SeenCount)
            .HasColumnName("seen_count")
            .HasDefaultValue(1)
            .IsRequired();

        builder.HasIndex(x => new { x.DeviceId, x.MessageType, x.DeduplicationKey })
            .IsUnique()
            .HasDatabaseName(UniqueDeduplicationIndexName);

        builder.HasIndex(x => x.ReceivedAtUtc)
            .HasDatabaseName("ix_upload_receive_registrations_received_at");
    }
}
