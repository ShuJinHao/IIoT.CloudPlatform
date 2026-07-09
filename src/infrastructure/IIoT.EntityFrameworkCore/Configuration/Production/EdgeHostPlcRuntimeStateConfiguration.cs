using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.EdgeHosts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.EntityFrameworkCore.Configuration.Production;

public sealed class EdgeHostPlcRuntimeStateConfiguration : IEntityTypeConfiguration<EdgeHostPlcRuntimeState>
{
    public void Configure(EntityTypeBuilder<EdgeHostPlcRuntimeState> builder)
    {
        builder.ToTable("edge_host_plc_runtime_states");

        builder.HasKey(state => state.Id);
        builder.Property(state => state.Id).HasColumnName("id");

        builder.Property(state => state.DeviceId)
            .IsRequired()
            .HasColumnName("device_id");

        builder.Property(state => state.ClientCode)
            .IsRequired()
            .HasMaxLength(EdgeHostPlcRuntimeState.ClientCodeMaxLength)
            .HasColumnName("client_code");

        builder.Property(state => state.PlcCode)
            .IsRequired()
            .HasMaxLength(EdgeHostPlcRuntimeState.PlcCodeMaxLength)
            .HasColumnName("plc_code");

        builder.Property(state => state.ReportedPlcName)
            .HasMaxLength(EdgeHostPlcRuntimeState.PlcNameMaxLength)
            .HasColumnName("reported_plc_name");

        builder.Property(state => state.IsConnected)
            .IsRequired()
            .HasColumnName("is_connected");

        builder.Property(state => state.RuntimeStatus)
            .IsRequired()
            .HasMaxLength(EdgeHostPlcRuntimeState.RuntimeStatusMaxLength)
            .HasColumnName("runtime_status");

        builder.Property(state => state.StationCode)
            .HasMaxLength(EdgeHostPlcRuntimeState.StationCodeMaxLength)
            .HasColumnName("station_code");

        builder.Property(state => state.Protocol)
            .HasMaxLength(EdgeHostPlcRuntimeState.ProtocolMaxLength)
            .HasColumnName("protocol");

        builder.Property(state => state.Address)
            .HasMaxLength(EdgeHostPlcRuntimeState.AddressMaxLength)
            .HasColumnName("address");

        builder.Property(state => state.LastError)
            .HasMaxLength(EdgeHostPlcRuntimeState.LastErrorMaxLength)
            .HasColumnName("last_error");

        builder.Property(state => state.LastSeenAtUtc)
            .IsRequired()
            .HasColumnName("last_seen_at_utc");

        builder.Property(state => state.CreatedAtUtc)
            .IsRequired()
            .HasColumnName("created_at_utc");

        builder.Property(state => state.UpdatedAtUtc)
            .IsRequired()
            .HasColumnName("updated_at_utc");

        builder.HasIndex(state => new { state.DeviceId, state.ClientCode, state.PlcCode })
            .IsUnique()
            .HasDatabaseName("ux_edge_host_plc_runtime_states_device_client_plc");

        builder.HasIndex(state => state.LastSeenAtUtc)
            .HasDatabaseName("ix_edge_host_plc_runtime_states_last_seen");

        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(state => state.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
