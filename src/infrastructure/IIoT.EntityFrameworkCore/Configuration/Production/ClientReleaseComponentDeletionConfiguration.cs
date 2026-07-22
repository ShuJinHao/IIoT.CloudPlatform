using IIoT.Core.Production.Aggregates.ClientReleases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.EntityFrameworkCore.Configuration.Production;

public sealed class ClientReleaseComponentDeletionConfiguration : IEntityTypeConfiguration<ClientReleaseComponentDeletion>
{
    public void Configure(EntityTypeBuilder<ClientReleaseComponentDeletion> builder)
    {
        builder.ToTable("edge_client_release_component_deletions");

        builder.HasKey(deletion => deletion.Id);
        builder.Property(deletion => deletion.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(deletion => deletion.ComponentId)
            .IsRequired()
            .HasColumnName("component_id");

        builder.Property(deletion => deletion.ComponentKind)
            .IsRequired()
            .HasMaxLength(32)
            .HasColumnName("component_kind");

        builder.Property(deletion => deletion.ComponentKey)
            .IsRequired()
            .HasMaxLength(128)
            .HasColumnName("component_key");

        builder.Property(deletion => deletion.Channel)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("channel");

        builder.Property(deletion => deletion.TargetRuntime)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("target_runtime");

        builder.Property(deletion => deletion.VersionsJson)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasColumnName("versions_json");

        builder.Property(deletion => deletion.Reason)
            .HasColumnType("text")
            .HasColumnName("reason");

        builder.Property(deletion => deletion.RequestedByUserId)
            .HasColumnName("requested_by_user_id");

        builder.Property(deletion => deletion.RequestedByUserName)
            .HasMaxLength(128)
            .HasColumnName("requested_by_user_name");

        builder.Property(deletion => deletion.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(24)
            .HasColumnName("status");

        builder.Property(deletion => deletion.FailureCode)
            .HasMaxLength(64)
            .HasColumnName("failure_code");

        builder.Property(deletion => deletion.RetryCount)
            .IsRequired()
            .HasColumnName("retry_count");

        builder.Property(deletion => deletion.CreatedAtUtc)
            .IsRequired()
            .HasColumnName("created_at_utc");

        builder.Property(deletion => deletion.UpdatedAtUtc)
            .IsRequired()
            .HasColumnName("updated_at_utc");

        builder.HasIndex(deletion => deletion.Status)
            .HasDatabaseName("ix_edge_client_release_component_deletions_status");

        builder.HasIndex(deletion => deletion.ComponentId)
            .HasDatabaseName("ix_edge_client_release_component_deletions_component");

        builder.Navigation(deletion => deletion.Files)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(deletion => deletion.Files)
            .WithOne()
            .HasForeignKey(file => file.ClientReleaseComponentDeletionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ClientReleaseComponentDeletionFileConfiguration : IEntityTypeConfiguration<ClientReleaseComponentDeletionFile>
{
    public void Configure(EntityTypeBuilder<ClientReleaseComponentDeletionFile> builder)
    {
        builder.ToTable("edge_client_release_component_deletion_files");

        builder.HasKey(file => file.Id);
        builder.Property(file => file.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(file => file.ClientReleaseComponentDeletionId)
            .IsRequired()
            .HasColumnName("deletion_id");

        builder.Property(file => file.RelativePath)
            .IsRequired()
            .HasMaxLength(1024)
            .HasColumnName("relative_path");

        builder.Property(file => file.ArtifactKind)
            .IsRequired()
            .HasMaxLength(32)
            .HasColumnName("artifact_kind");

        builder.Property(file => file.Sha256)
            .HasMaxLength(64)
            .HasColumnName("sha256");

        builder.Property(file => file.SizeBytes)
            .HasColumnName("size_bytes");

        builder.HasIndex(file => file.ClientReleaseComponentDeletionId)
            .HasDatabaseName("ix_edge_client_release_component_deletion_files_deletion");
    }
}
