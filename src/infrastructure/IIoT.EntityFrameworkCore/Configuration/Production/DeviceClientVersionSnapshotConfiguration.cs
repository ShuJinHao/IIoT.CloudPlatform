using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.EntityFrameworkCore.Configuration.Production;

public sealed class DeviceClientVersionSnapshotConfiguration : IEntityTypeConfiguration<DeviceClientVersionSnapshot>
{
    public void Configure(EntityTypeBuilder<DeviceClientVersionSnapshot> builder)
    {
        builder.ToTable("edge_device_client_version_snapshots");

        builder.HasKey(snapshot => snapshot.Id);
        builder.Property(snapshot => snapshot.Id).HasColumnName("id");

        builder.Property(snapshot => snapshot.DeviceId)
            .IsRequired()
            .HasColumnName("device_id");

        builder.Property(snapshot => snapshot.ClientCode)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("client_code");

        builder.Property(snapshot => snapshot.HostVersion)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("host_version");

        builder.Property(snapshot => snapshot.HostApiVersion)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("host_api_version");

        builder.Property(snapshot => snapshot.Channel)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("channel");

        builder.Property(snapshot => snapshot.ReportedAtUtc)
            .IsRequired()
            .HasColumnName("reported_at_utc");

        builder.Property(snapshot => snapshot.ReceivedAtUtc)
            .IsRequired()
            .HasColumnName("received_at_utc");

        builder.Property(snapshot => snapshot.LocalIpAddressesJson)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasColumnName("local_ip_addresses_json");

        builder.Property(snapshot => snapshot.RemoteIpAddress)
            .HasMaxLength(128)
            .HasColumnName("remote_ip_address");

        builder.HasIndex(snapshot => snapshot.DeviceId)
            .IsUnique()
            .HasDatabaseName("ux_edge_device_client_version_snapshots_device");

        builder.HasOne<Device>()
            .WithOne()
            .HasForeignKey<DeviceClientVersionSnapshot>(snapshot => snapshot.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(snapshot => snapshot.InstalledPlugins)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(snapshot => snapshot.InstalledPlugins)
            .WithOne()
            .HasForeignKey(plugin => plugin.DeviceClientVersionSnapshotId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
