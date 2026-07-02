using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.EntityFrameworkCore.Configuration.Production;

public sealed class DeviceClientStateConfiguration : IEntityTypeConfiguration<DeviceClientState>
{
    public void Configure(EntityTypeBuilder<DeviceClientState> builder)
    {
        builder.ToTable("edge_device_client_states");

        builder.HasKey(state => state.Id);
        builder.Property(state => state.Id).HasColumnName("id");

        builder.Property(state => state.DeviceId)
            .IsRequired()
            .HasColumnName("device_id");

        builder.Property(state => state.ClientCode)
            .IsRequired()
            .HasMaxLength(64)
            .HasColumnName("client_code");

        builder.Property(state => state.Channel)
            .HasMaxLength(64)
            .HasColumnName("channel");

        builder.Property(state => state.HostVersion)
            .HasMaxLength(64)
            .HasColumnName("host_version");

        builder.Property(state => state.HostApiVersion)
            .HasMaxLength(64)
            .HasColumnName("host_api_version");

        builder.Property(state => state.VersionLocalIpAddressesJson)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasColumnName("version_local_ip_addresses_json");

        builder.Property(state => state.VersionRemoteIpAddress)
            .HasMaxLength(128)
            .HasColumnName("version_remote_ip_address");

        builder.Property(state => state.VersionReportedAtUtc)
            .HasColumnName("version_reported_at_utc");

        builder.Property(state => state.VersionReceivedAtUtc)
            .HasColumnName("version_received_at_utc");

        builder.Property(state => state.RuntimeInstanceId)
            .HasMaxLength(128)
            .HasColumnName("runtime_instance_id");

        builder.Property(state => state.MachineProfile)
            .HasMaxLength(128)
            .HasColumnName("machine_profile");

        builder.Property(state => state.RuntimeHostVersion)
            .HasMaxLength(64)
            .HasColumnName("runtime_host_version");

        builder.Property(state => state.RuntimeHostApiVersion)
            .HasMaxLength(64)
            .HasColumnName("runtime_host_api_version");

        builder.Property(state => state.RuntimeStatus)
            .HasMaxLength(24)
            .HasColumnName("runtime_status");

        builder.Property(state => state.RuntimeLocalIpAddressesJson)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasColumnName("runtime_local_ip_addresses_json");

        builder.Property(state => state.RuntimeRemoteIpAddress)
            .HasMaxLength(128)
            .HasColumnName("runtime_remote_ip_address");

        builder.Property(state => state.RuntimeStartedAtUtc)
            .HasColumnName("runtime_started_at_utc");

        builder.Property(state => state.LastRuntimeHeartbeatAtUtc)
            .HasColumnName("last_runtime_heartbeat_at_utc");

        builder.Property(state => state.LastRuntimeStoppedAtUtc)
            .HasColumnName("last_runtime_stopped_at_utc");

        builder.Property(state => state.CreatedAtUtc)
            .IsRequired()
            .HasColumnName("created_at_utc");

        builder.Property(state => state.UpdatedAtUtc)
            .IsRequired()
            .HasColumnName("updated_at_utc");

        builder.HasIndex(state => new { state.DeviceId, state.ClientCode })
            .IsUnique()
            .HasDatabaseName("ux_edge_device_client_states_device_client");

        builder.HasIndex(state => state.DeviceId)
            .HasDatabaseName("ix_edge_device_client_states_device");

        builder.HasIndex(state => state.LastRuntimeHeartbeatAtUtc)
            .HasDatabaseName("ix_edge_device_client_states_last_runtime_heartbeat");

        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(state => state.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
