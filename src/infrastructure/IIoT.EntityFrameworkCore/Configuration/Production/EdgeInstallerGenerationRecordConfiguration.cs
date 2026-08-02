using IIoT.Core.Production.Aggregates.ClientReleases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.EntityFrameworkCore.Configuration.Production;

public sealed class EdgeInstallerGenerationRecordConfiguration
    : IEntityTypeConfiguration<EdgeInstallerGenerationRecord>
{
    public void Configure(EntityTypeBuilder<EdgeInstallerGenerationRecord> builder)
    {
        builder.ToTable("edge_installer_generation_records");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id)
            .ValueGeneratedNever()
            .HasColumnName("generation_id");
        builder.Property(record => record.OperatorUserId)
            .HasColumnName("operator_user_id");
        builder.Property(record => record.OperatorName)
            .HasMaxLength(128)
            .HasColumnName("operator_name");
        builder.Property(record => record.GeneratedAtUtc)
            .IsRequired()
            .HasColumnName("generated_at_utc");
        builder.Property(record => record.Channel)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("channel");
        builder.Property(record => record.TargetRuntime)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("target_runtime");
        builder.Property(record => record.HostVersion)
            .IsRequired()
            .HasMaxLength(128)
            .HasColumnName("host_version");
        builder.Property(record => record.HostSha256)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("host_sha256");
        builder.Property(record => record.FileName)
            .IsRequired()
            .HasMaxLength(256)
            .HasColumnName("file_name");
        builder.Property(record => record.PackageSha256)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("package_sha256");
        builder.Property(record => record.PackageSize)
            .IsRequired()
            .HasColumnName("package_size");
        builder.Property(record => record.BindingsJson)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasColumnName("bindings_json");
        builder.Property(record => record.PluginsJson)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasColumnName("plugins_json");

        builder.HasIndex(record => record.GeneratedAtUtc)
            .HasDatabaseName("ix_edge_installer_generation_records_generated_at");
        builder.HasIndex(record => record.OperatorUserId)
            .HasDatabaseName("ix_edge_installer_generation_records_operator");
    }
}
