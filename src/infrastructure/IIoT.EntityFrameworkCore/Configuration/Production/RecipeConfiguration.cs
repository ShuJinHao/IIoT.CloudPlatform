using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using IIoT.Core.Production.Aggregates.Recipes;

namespace IIoT.EntityFrameworkCore.Configuration.Production;

/// <summary>
/// 工艺配方实体的 EF Core 数据库映射配置 (包含 JSONB 高级映射)
/// </summary>
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

        // 特调设备标识：这是一个可空的 Guid (Guid?)
        builder.Property(r => r.DeviceId)
            .HasColumnName("device_id");

        builder.Property(r => r.IsActive)
            .IsRequired()
            .HasColumnName("is_active");

        // 🌟🌟 终极杀招：强制将字符串映射为 PostgreSQL 的原生 jsonb 类型
        // 这样在数据库里可以直接对 JSON 内部的非标参数进行高效查询和更新
        builder.Property(r => r.ParametersJsonb)
            .IsRequired()
            .HasColumnType("jsonb")
            .HasColumnName("parameters_jsonb");

        // 核心复合索引：WPF 获取配方时，必定是同时根据 ProcessId 和 DeviceId 查找
        builder.HasIndex(r => new { r.ProcessId, r.DeviceId })
            .HasDatabaseName("ix_recipes_process_device");
    }
}