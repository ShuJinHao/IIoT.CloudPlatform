using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.EntityFrameworkCore.Configuration.Production;

public sealed class EdgeDeviceRuntimeHeartbeatConfiguration : IEntityTypeConfiguration<EdgeDeviceRuntimeHeartbeat>
{
    public void Configure(EntityTypeBuilder<EdgeDeviceRuntimeHeartbeat> builder)
    {
        builder.ToTable("edge_device_runtime_heartbeats");

        builder.HasKey(heartbeat => heartbeat.Id);
        builder.Property(heartbeat => heartbeat.Id).HasColumnName("id");

        builder.Property(heartbeat => heartbeat.DeviceId)
            .IsRequired()
            .HasColumnName("device_id");

        builder.Property(heartbeat => heartbeat.ClientCode)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("client_code");

        builder.Property(heartbeat => heartbeat.RuntimeInstanceId)
            .IsRequired()
            .HasMaxLength(128)
            .HasColumnName("runtime_instance_id");

        builder.Property(heartbeat => heartbeat.MachineProfile)
            .HasMaxLength(128)
            .HasColumnName("machine_profile");

        builder.Property(heartbeat => heartbeat.HostVersion)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("host_version");

        builder.Property(heartbeat => heartbeat.HostApiVersion)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("host_api_version");

        builder.Property(heartbeat => heartbeat.Status)
            .IsRequired()
            .HasMaxLength(24)
            .HasColumnName("status");

        builder.Property(heartbeat => heartbeat.LocalIpAddressesJson)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasColumnName("local_ip_addresses_json");

        builder.Property(heartbeat => heartbeat.RemoteIpAddress)
            .HasMaxLength(128)
            .HasColumnName("remote_ip_address");

        builder.Property(heartbeat => heartbeat.StartedAtUtc)
            .IsRequired()
            .HasColumnName("started_at_utc");

        builder.Property(heartbeat => heartbeat.LastHeartbeatAtUtc)
            .IsRequired()
            .HasColumnName("last_heartbeat_at_utc");

        builder.Property(heartbeat => heartbeat.LastStoppedAtUtc)
            .HasColumnName("last_stopped_at_utc");

        builder.Property(heartbeat => heartbeat.CreatedAtUtc)
            .IsRequired()
            .HasColumnName("created_at_utc");

        builder.Property(heartbeat => heartbeat.UpdatedAtUtc)
            .IsRequired()
            .HasColumnName("updated_at_utc");

        builder.HasIndex(heartbeat => new { heartbeat.DeviceId, heartbeat.ClientCode })
            .IsUnique()
            .HasDatabaseName("ux_edge_device_runtime_heartbeats_device_client");

        builder.HasIndex(heartbeat => heartbeat.LastHeartbeatAtUtc)
            .HasDatabaseName("ix_edge_device_runtime_heartbeats_last_heartbeat");

        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(heartbeat => heartbeat.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
