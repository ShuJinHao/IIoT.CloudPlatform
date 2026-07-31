using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.EntityFrameworkCore.Uploads;

internal sealed class UploadReceiveObservationConfiguration
    : IEntityTypeConfiguration<UploadReceiveObservation>
{
    public void Configure(
        EntityTypeBuilder<UploadReceiveObservation> builder)
    {
        builder.ToTable("upload_receive_observations");

        builder.HasKey(observation => observation.Id);

        builder.Property(observation => observation.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(observation => observation.RegistrationId)
            .HasColumnName("registration_id")
            .IsRequired();

        builder.Property(observation => observation.SeenAtUtc)
            .HasColumnName("seen_at_utc")
            .IsRequired();

        builder.HasIndex(observation => observation.RegistrationId)
            .HasDatabaseName(
                "ix_upload_receive_observations_registration_id");

        builder.HasIndex(observation => observation.SeenAtUtc)
            .HasDatabaseName(
                "ix_upload_receive_observations_seen_at_utc");

        builder.HasOne<UploadReceiveRegistration>()
            .WithMany()
            .HasForeignKey(observation => observation.RegistrationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
