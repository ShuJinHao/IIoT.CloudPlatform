using IIoT.Core.Production.Aggregates.ClientReleases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.EntityFrameworkCore.Configuration.Production;

public sealed class ClientReleaseRetentionPolicyConfiguration : IEntityTypeConfiguration<ClientReleaseRetentionPolicy>
{
    public void Configure(EntityTypeBuilder<ClientReleaseRetentionPolicy> builder)
    {
        builder.ToTable("edge_client_release_retention_policies");

        builder.HasKey(policy => policy.Id);
        builder.Property(policy => policy.Id).HasColumnName("id");

        builder.Property(policy => policy.MaxVersionsPerComponent)
            .IsRequired()
            .HasColumnName("max_versions_per_component");

        builder.Property(policy => policy.UpdatedAtUtc)
            .IsRequired()
            .HasColumnName("updated_at_utc");
    }
}
