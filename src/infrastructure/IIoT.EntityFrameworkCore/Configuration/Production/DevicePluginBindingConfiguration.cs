using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.EntityFrameworkCore.Configuration.Production;

public sealed class DevicePluginBindingConfiguration
    : IEntityTypeConfiguration<DevicePluginBinding>
{
    public void Configure(EntityTypeBuilder<DevicePluginBinding> builder)
    {
        builder.ToTable("device_plugin_bindings");
        builder.HasKey(binding => binding.Id);
        builder.Property(binding => binding.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");
        builder.Property(binding => binding.DeviceId)
            .IsRequired()
            .HasColumnName("device_id");
        builder.Property(binding => binding.ClientReleaseComponentId)
            .IsRequired()
            .HasColumnName("client_release_component_id");
        builder.Property(binding => binding.ProcessType)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("process_type");
        builder.Property(binding => binding.BoundAtUtc)
            .IsRequired()
            .HasColumnName("bound_at_utc");
        builder.Property(binding => binding.RowVersion)
            .HasColumnName("xmin")
            .IsRowVersion();

        builder.HasIndex(binding => binding.DeviceId)
            .IsUnique()
            .HasDatabaseName("ux_device_plugin_bindings_device");
        builder.HasIndex(binding => binding.ClientReleaseComponentId)
            .IsUnique()
            .HasDatabaseName("ux_device_plugin_bindings_component");

        builder.HasOne<Device>()
            .WithOne()
            .HasForeignKey<DevicePluginBinding>(binding => binding.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ClientReleaseComponent>()
            .WithOne()
            .HasForeignKey<DevicePluginBinding>(binding => binding.ClientReleaseComponentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
