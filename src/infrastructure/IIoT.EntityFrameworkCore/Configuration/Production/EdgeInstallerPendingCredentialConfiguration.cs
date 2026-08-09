using IIoT.Core.Production.Aggregates.ClientReleases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.EntityFrameworkCore.Configuration.Production;

public sealed class EdgeInstallerPendingCredentialConfiguration
    : IEntityTypeConfiguration<EdgeInstallerPendingCredential>
{
    public void Configure(EntityTypeBuilder<EdgeInstallerPendingCredential> builder)
    {
        builder.ToTable("edge_installer_pending_credentials");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.ClientCode).HasMaxLength(64).IsRequired();
        builder.Property(item => item.SecretHash).HasMaxLength(256).IsRequired();
        builder.Property(item => item.ModuleId).HasMaxLength(128).IsRequired();
        builder.Property(item => item.PluginVersion).HasMaxLength(128).IsRequired();
        builder.Property(item => item.PackageSha256).HasMaxLength(64).IsRequired();
        builder.Property(item => item.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(item => item.ActivationReplayCount).IsRequired();
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.GenerationId, item.DeviceId })
            .IsUnique();
        builder.HasIndex(item => new { item.ClientCode, item.Status, item.ExpiresAtUtc });
        builder.HasOne<EdgeInstallerGenerationRecord>()
            .WithMany()
            .HasForeignKey(item => item.GenerationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IIoT.Core.Production.Aggregates.Devices.Device>()
            .WithMany()
            .HasForeignKey(item => item.DeviceId)
            // Administrator hard-delete revokes device credentials while the
            // immutable generation record remains as installation evidence.
            .OnDelete(DeleteBehavior.Cascade);
    }
}
