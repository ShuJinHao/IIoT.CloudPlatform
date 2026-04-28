using IIoT.EntityFrameworkCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.EntityFrameworkCore.Configuration.Identity;

public sealed class RefreshTokenSessionConfiguration : IEntityTypeConfiguration<RefreshTokenSession>
{
    public void Configure(EntityTypeBuilder<RefreshTokenSession> builder)
    {
        builder.ToTable("refresh_token_sessions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ActorType)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.TokenHash)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.RevokedReason)
            .HasMaxLength(256);

        builder.HasIndex(x => x.TokenHash)
            .IsUnique();

        builder.HasIndex(x => new { x.ActorType, x.SubjectId });

        builder.HasIndex(x => x.ExpiresAtUtc);
    }
}
