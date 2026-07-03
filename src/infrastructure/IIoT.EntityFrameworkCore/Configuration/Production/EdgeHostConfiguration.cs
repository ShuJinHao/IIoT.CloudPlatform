using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.EdgeHosts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.EntityFrameworkCore.Configuration.Production;

public sealed class EdgeHostConfiguration : IEntityTypeConfiguration<EdgeHost>
{
    public void Configure(EntityTypeBuilder<EdgeHost> builder)
    {
        builder.ToTable("edge_hosts");

        builder.HasKey(host => host.Id);
        builder.Property(host => host.Id).HasColumnName("id");

        builder.Property(host => host.DeviceId)
            .IsRequired()
            .HasColumnName("device_id");

        builder.Property(host => host.ClientCode)
            .IsRequired()
            .HasMaxLength(EdgeHost.ClientCodeMaxLength)
            .HasColumnName("client_code");

        builder.Property(host => host.HostName)
            .IsRequired()
            .HasMaxLength(EdgeHost.HostNameMaxLength)
            .HasColumnName("host_name");

        builder.Property(host => host.Enabled)
            .IsRequired()
            .HasColumnName("enabled");

        builder.Property(host => host.Remark)
            .HasMaxLength(EdgeHost.RemarkMaxLength)
            .HasColumnName("remark");

        builder.Property(host => host.CreatedAtUtc)
            .IsRequired()
            .HasColumnName("created_at_utc");

        builder.Property(host => host.UpdatedAtUtc)
            .IsRequired()
            .HasColumnName("updated_at_utc");

        builder.HasIndex(host => host.DeviceId)
            .IsUnique()
            .HasDatabaseName("ux_edge_hosts_device_id");

        builder.HasIndex(host => host.ClientCode)
            .IsUnique()
            .HasDatabaseName("ux_edge_hosts_client_code");

        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(host => host.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(host => host.PlcBindings)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(host => host.PlcBindings)
            .WithOne()
            .HasForeignKey(binding => binding.EdgeHostId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class EdgeHostPlcBindingConfiguration : IEntityTypeConfiguration<EdgeHostPlcBinding>
{
    public void Configure(EntityTypeBuilder<EdgeHostPlcBinding> builder)
    {
        builder.ToTable("edge_host_plc_bindings");

        builder.HasKey(binding => binding.Id);
        builder.Property(binding => binding.Id).HasColumnName("id");

        builder.Property(binding => binding.EdgeHostId)
            .IsRequired()
            .HasColumnName("edge_host_id");

        builder.Property(binding => binding.PlcCode)
            .IsRequired()
            .HasMaxLength(EdgeHostPlcBinding.PlcCodeMaxLength)
            .HasColumnName("plc_code");

        builder.Property(binding => binding.PlcName)
            .IsRequired()
            .HasMaxLength(EdgeHostPlcBinding.PlcNameMaxLength)
            .HasColumnName("plc_name");

        builder.Property(binding => binding.ProcessId)
            .HasColumnName("process_id");

        builder.Property(binding => binding.BusinessDeviceId)
            .HasColumnName("business_device_id");

        builder.Property(binding => binding.StationCode)
            .HasMaxLength(EdgeHostPlcBinding.StationCodeMaxLength)
            .HasColumnName("station_code");

        builder.Property(binding => binding.Protocol)
            .HasMaxLength(EdgeHostPlcBinding.ProtocolMaxLength)
            .HasColumnName("protocol");

        builder.Property(binding => binding.Address)
            .HasMaxLength(EdgeHostPlcBinding.AddressMaxLength)
            .HasColumnName("address");

        builder.Property(binding => binding.Enabled)
            .IsRequired()
            .HasColumnName("enabled");

        builder.Property(binding => binding.DisplayOrder)
            .IsRequired()
            .HasColumnName("display_order");

        builder.Property(binding => binding.Remark)
            .HasMaxLength(EdgeHostPlcBinding.RemarkMaxLength)
            .HasColumnName("remark");

        builder.Property(binding => binding.CreatedAtUtc)
            .IsRequired()
            .HasColumnName("created_at_utc");

        builder.Property(binding => binding.UpdatedAtUtc)
            .IsRequired()
            .HasColumnName("updated_at_utc");

        builder.HasIndex(binding => new { binding.EdgeHostId, binding.PlcCode })
            .IsUnique()
            .HasDatabaseName("ux_edge_host_plc_bindings_host_plc");

        builder.HasIndex(binding => binding.ProcessId)
            .HasDatabaseName("ix_edge_host_plc_bindings_process_id");

        builder.HasIndex(binding => binding.BusinessDeviceId)
            .HasDatabaseName("ix_edge_host_plc_bindings_business_device_id");

        builder.HasOne<MfgProcess>()
            .WithMany()
            .HasForeignKey(binding => binding.ProcessId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(binding => binding.BusinessDeviceId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
