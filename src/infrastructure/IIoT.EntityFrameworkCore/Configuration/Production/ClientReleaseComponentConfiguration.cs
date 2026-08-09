using IIoT.Core.Production.Aggregates.ClientReleases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.EntityFrameworkCore.Configuration.Production;

public sealed class ClientReleaseComponentConfiguration : IEntityTypeConfiguration<ClientReleaseComponent>
{
    public void Configure(EntityTypeBuilder<ClientReleaseComponent> builder)
    {
        builder.ToTable("edge_client_release_components");

        builder.HasKey(component => component.Id);
        builder.Property(component => component.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(component => component.ComponentKind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasColumnName("component_kind");

        builder.Property(component => component.ComponentKey)
            .IsRequired()
            .HasMaxLength(128)
            .HasColumnName("component_key");

        builder.Property(component => component.DisplayName)
            .IsRequired()
            .HasMaxLength(128)
            .HasColumnName("display_name");

        builder.Property(component => component.Description)
            .HasMaxLength(512)
            .HasColumnName("description");

        builder.Property(component => component.IconKind)
            .HasMaxLength(64)
            .HasColumnName("icon_kind");

        builder.Property(component => component.AccentColor)
            .HasMaxLength(32)
            .HasColumnName("accent_color");

        builder.Property(component => component.Channel)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("channel");

        builder.Property(component => component.TargetRuntime)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("target_runtime");

        builder.Property(component => component.SupportedProcessType)
            .HasMaxLength(50)
            .HasColumnName("supported_process_type");
        builder.Property(component => component.BusinessDocumentRef)
            .HasMaxLength(512)
            .HasColumnName("business_document_ref");
        builder.Property(component => component.ManifestSchemaVersion)
            .IsRequired()
            .HasDefaultValue(1)
            .HasColumnName("manifest_schema_version");
        builder.Property(component => component.FileManifestSha256)
            .HasMaxLength(64)
            .HasColumnName("file_manifest_sha256");
        builder.Property(component => component.DataCapabilitiesJson)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasDefaultValue("[]")
            .HasColumnName("data_capabilities_json");
        builder.Property(component => component.WasEverDeviceBound)
            .IsRequired()
            .HasDefaultValue(false)
            .HasColumnName("was_ever_device_bound");

        builder.Property(component => component.CreatedAtUtc)
            .IsRequired()
            .HasColumnName("created_at_utc");

        builder.Property(component => component.UpdatedAtUtc)
            .IsRequired()
            .HasColumnName("updated_at_utc");

        builder.Property(component => component.RowVersion)
            .HasColumnName("xmin")
            .IsRowVersion();

        builder.HasIndex(component => new
            {
                component.ComponentKind,
                component.ComponentKey,
                component.Channel,
                component.TargetRuntime
            })
            .IsUnique()
            .HasDatabaseName("ux_edge_client_release_components_identity");

        builder.HasIndex(component => new
            {
                component.Channel,
                component.TargetRuntime,
                component.ComponentKind
            })
            .HasDatabaseName("ix_edge_client_release_components_catalog");

        builder.Navigation(component => component.Versions)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(component => component.Versions)
            .WithOne()
            .HasForeignKey(version => version.ClientReleaseComponentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ClientReleaseVersionConfiguration : IEntityTypeConfiguration<ClientReleaseVersion>
{
    public void Configure(EntityTypeBuilder<ClientReleaseVersion> builder)
    {
        builder.ToTable("edge_client_release_versions");

        builder.HasKey(version => version.Id);
        builder.Property(version => version.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(version => version.ClientReleaseComponentId)
            .IsRequired()
            .HasColumnName("component_id");

        builder.Property(version => version.Version)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("version");

        builder.Property(version => version.HostApiVersion)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("host_api_version");

        builder.Property(version => version.MinHostVersion)
            .HasMaxLength(64)
            .HasColumnName("min_host_version");

        builder.Property(version => version.MaxHostVersion)
            .HasMaxLength(64)
            .HasColumnName("max_host_version");

        builder.Property(version => version.TargetFramework)
            .HasMaxLength(64)
            .HasColumnName("target_framework");

        builder.Property(version => version.DownloadUrl)
            .IsRequired()
            .HasMaxLength(1024)
            .HasColumnName("download_url");

        builder.Property(version => version.Sha256)
            .IsRequired()
            .HasMaxLength(128)
            .HasColumnName("sha256");

        builder.Property(version => version.PackageSize)
            .IsRequired()
            .HasColumnName("package_size");

        builder.Property(version => version.ReleaseNotes)
            .HasColumnType("text")
            .HasColumnName("release_notes");

        builder.Property(version => version.DependenciesJson)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasColumnName("dependencies_json");

        builder.Property(version => version.DataCapabilitiesJson)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasColumnName("data_capabilities_json");

        builder.Property(version => version.FileManifestSha256)
            .HasMaxLength(64)
            .HasColumnName("file_manifest_sha256");

        builder.Property(version => version.DependencyClosureSha256)
            .HasMaxLength(64)
            .HasColumnName("dependency_closure_sha256");

        builder.Property(version => version.DependencyHostVersion)
            .HasMaxLength(64)
            .HasColumnName("dependency_host_version");

        builder.Property(version => version.DependencyHostFileManifestSha256)
            .HasMaxLength(64)
            .HasColumnName("dependency_host_file_manifest_sha256");

        builder.Property(version => version.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(24)
            .HasColumnName("status");

        builder.Property(version => version.Signature)
            .HasColumnType("text")
            .HasColumnName("signature");

        builder.Property(version => version.Publisher)
            .HasMaxLength(128)
            .HasColumnName("publisher");

        builder.Property(version => version.CreatedAtUtc)
            .IsRequired()
            .HasColumnName("created_at_utc");

        builder.Property(version => version.PublishedAtUtc)
            .HasColumnName("published_at_utc");

        builder.Property(version => version.DeletedAtUtc)
            .HasColumnName("deleted_at_utc");

        builder.Property(version => version.DeletionReason)
            .HasColumnType("text")
            .HasColumnName("deletion_reason");

        builder.Property(version => version.DeletionFailure)
            .HasColumnType("text")
            .HasColumnName("deletion_failure");

        builder.Property(version => version.DeletionReceiptJson)
            .HasColumnType("jsonb")
            .HasColumnName("deletion_receipt_json");

        builder.Property(version => version.RowVersion)
            .HasColumnName("xmin")
            .IsRowVersion();

        builder.HasIndex(version => new { version.ClientReleaseComponentId, version.Version })
            .IsUnique()
            .HasDatabaseName("ux_edge_client_release_versions_component_version");

        builder.HasIndex(version => version.Status)
            .HasDatabaseName("ix_edge_client_release_versions_status");

        builder.Navigation(version => version.Artifacts)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(version => version.Artifacts)
            .WithOne()
            .HasForeignKey(artifact => artifact.ClientReleaseVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ClientReleaseArtifactConfiguration : IEntityTypeConfiguration<ClientReleaseArtifact>
{
    public void Configure(EntityTypeBuilder<ClientReleaseArtifact> builder)
    {
        builder.ToTable("edge_client_release_artifacts");

        builder.HasKey(artifact => artifact.Id);
        builder.Property(artifact => artifact.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(artifact => artifact.ClientReleaseVersionId)
            .IsRequired()
            .HasColumnName("release_version_id");

        builder.Property(artifact => artifact.ArtifactKind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(48)
            .HasColumnName("artifact_kind");

        builder.Property(artifact => artifact.RelativePath)
            .IsRequired()
            .HasMaxLength(1024)
            .HasColumnName("relative_path");

        builder.Property(artifact => artifact.Sha256)
            .HasMaxLength(128)
            .HasColumnName("sha256");

        builder.Property(artifact => artifact.Size)
            .HasColumnName("size");

        builder.Property(artifact => artifact.CreatedAtUtc)
            .IsRequired()
            .HasColumnName("created_at_utc");

        builder.HasIndex(artifact => new
            {
                artifact.ClientReleaseVersionId,
                artifact.ArtifactKind,
                artifact.RelativePath
            })
            .IsUnique()
            .HasDatabaseName("ux_edge_client_release_artifacts_version_path");
    }
}
