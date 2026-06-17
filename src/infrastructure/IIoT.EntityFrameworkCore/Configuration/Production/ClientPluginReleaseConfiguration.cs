using IIoT.Core.Production.Aggregates.ClientReleases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.EntityFrameworkCore.Configuration.Production;

public sealed class ClientPluginReleaseConfiguration : IEntityTypeConfiguration<ClientPluginRelease>
{
    public void Configure(EntityTypeBuilder<ClientPluginRelease> builder)
    {
        builder.ToTable("edge_client_plugin_releases");

        builder.HasKey(release => release.Id);
        builder.Property(release => release.Id).HasColumnName("id");

        builder.Property(release => release.ModuleId)
            .IsRequired()
            .HasMaxLength(128)
            .HasColumnName("module_id");

        builder.Property(release => release.DisplayName)
            .IsRequired()
            .HasMaxLength(128)
            .HasColumnName("display_name");

        builder.Property(release => release.Description)
            .HasMaxLength(512)
            .HasColumnName("description");

        builder.Property(release => release.IconKind)
            .HasMaxLength(64)
            .HasColumnName("icon_kind");

        builder.Property(release => release.AccentColor)
            .HasMaxLength(32)
            .HasColumnName("accent_color");

        builder.Property(release => release.Channel)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("channel");

        builder.Property(release => release.Version)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("version");

        builder.Property(release => release.HostApiVersion)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("host_api_version");

        builder.Property(release => release.MinHostVersion)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("min_host_version");

        builder.Property(release => release.MaxHostVersion)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("max_host_version");

        builder.Property(release => release.TargetRuntime)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("target_runtime");

        builder.Property(release => release.TargetFramework)
            .HasMaxLength(64)
            .HasColumnName("target_framework");

        builder.Property(release => release.DownloadUrl)
            .IsRequired()
            .HasMaxLength(1024)
            .HasColumnName("download_url");

        builder.Property(release => release.Sha256)
            .IsRequired()
            .HasMaxLength(128)
            .HasColumnName("sha256");

        builder.Property(release => release.PackageSize)
            .HasColumnName("package_size");

        builder.Property(release => release.ReleaseNotes)
            .HasColumnType("text")
            .HasColumnName("release_notes");

        builder.Property(release => release.DependenciesJson)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasColumnName("dependencies_json");

        builder.Property(release => release.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(24)
            .HasColumnName("status");

        builder.Property(release => release.Signature)
            .HasColumnType("text")
            .HasColumnName("signature");

        builder.Property(release => release.Publisher)
            .HasMaxLength(128)
            .HasColumnName("publisher");

        builder.Property(release => release.CreatedAtUtc)
            .IsRequired()
            .HasColumnName("created_at_utc");

        builder.Property(release => release.PublishedAtUtc)
            .HasColumnName("published_at_utc");

        builder.HasIndex(release => new { release.ModuleId, release.Channel, release.Version, release.TargetRuntime })
            .IsUnique()
            .HasDatabaseName("ux_edge_client_plugin_releases_identity");

        builder.HasIndex(release => new { release.Channel, release.TargetRuntime, release.Status })
            .HasDatabaseName("ix_edge_client_plugin_releases_catalog");
    }
}
