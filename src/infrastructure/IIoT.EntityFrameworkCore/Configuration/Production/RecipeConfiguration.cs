using IIoT.Core.Production.Aggregates.Recipes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.EntityFrameworkCore.Configuration.Production;

public class RecipeConfiguration : IEntityTypeConfiguration<Recipe>
{
    public void Configure(EntityTypeBuilder<Recipe> builder)
    {
        builder.ToTable("recipes");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");

        builder.Property(r => r.RecipeName)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("recipe_name");

        builder.Property(r => r.Version)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("version");

        builder.Property(r => r.ProcessId)
            .IsRequired()
            .HasColumnName("process_id");

        builder.Property(r => r.DeviceId)
            .IsRequired()
            .HasColumnName("device_id");

        builder.Property(r => r.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasColumnName("status");

        builder.Property(r => r.ParametersJsonb)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasColumnName("parameters_jsonb");

        builder.Property(r => r.RowVersion)
            .HasColumnName("xmin")
            .IsRowVersion();

        builder.HasIndex(r => new { r.ProcessId, r.DeviceId })
            .HasDatabaseName("ix_recipes_process_device");

        builder.HasIndex(r => r.DeviceId)
            .HasDatabaseName("ix_recipes_device_id");

        builder.HasIndex(r => new { r.RecipeName, r.Version })
            .HasDatabaseName("ix_recipes_name_version");
    }
}
