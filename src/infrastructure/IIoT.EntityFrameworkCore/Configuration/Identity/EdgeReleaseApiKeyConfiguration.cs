using IIoT.EntityFrameworkCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.EntityFrameworkCore.Configuration.Identity;

internal sealed class EdgeReleaseApiKeyConfiguration : IEntityTypeConfiguration<EdgeReleaseApiKey>
{
    public void Configure(EntityTypeBuilder<EdgeReleaseApiKey> builder)
    {
        builder.ToTable("edge_release_api_keys");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.KeyHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.PermissionsJson)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.RevokedReason)
            .HasMaxLength(256);

        builder.HasIndex(x => x.Name)
            .IsUnique();
        builder.HasIndex(x => x.KeyHash)
            .IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.ExpiresAtUtc);
        builder.HasIndex(x => x.LastUsedAtUtc);
    }
}
