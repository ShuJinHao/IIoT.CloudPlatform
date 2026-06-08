using IIoT.Core.Production.Aggregates.ClientReleases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.EntityFrameworkCore.Configuration.Production;

public sealed class DeviceClientPluginVersionConfiguration : IEntityTypeConfiguration<DeviceClientPluginVersion>
{
    public void Configure(EntityTypeBuilder<DeviceClientPluginVersion> builder)
    {
        builder.ToTable("edge_device_client_plugin_versions");

        builder.HasKey(plugin => plugin.Id);
        builder.Property(plugin => plugin.Id).HasColumnName("id");

        builder.Property(plugin => plugin.DeviceClientVersionSnapshotId)
            .IsRequired()
            .HasColumnName("device_client_version_snapshot_id");

        builder.Property(plugin => plugin.ModuleId)
            .IsRequired()
            .HasMaxLength(128)
            .HasColumnName("module_id");

        builder.Property(plugin => plugin.DisplayName)
            .HasMaxLength(128)
            .HasColumnName("display_name");

        builder.Property(plugin => plugin.Version)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("version");

        builder.Property(plugin => plugin.HostApiVersion)
            .HasMaxLength(64)
            .HasColumnName("host_api_version");

        builder.Property(plugin => plugin.Enabled)
            .IsRequired()
            .HasColumnName("enabled");

        builder.HasIndex(plugin => new { plugin.DeviceClientVersionSnapshotId, plugin.ModuleId })
            .IsUnique()
            .HasDatabaseName("ux_edge_device_client_plugin_versions_module");
    }
}
