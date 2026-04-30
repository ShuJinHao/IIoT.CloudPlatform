using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IIoT.EntityFrameworkCore.Configuration.MasterData;

public class MfgProcessConfiguration : IEntityTypeConfiguration<MfgProcess>
{
    public void Configure(EntityTypeBuilder<MfgProcess> builder)
    {
        builder.ToTable("mfg_processes");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.ProcessCode)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("process_code");

        builder.Property(p => p.ProcessName)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("process_name");

        builder.Property(p => p.RowVersion)
            .IsRowVersion()
            .HasColumnName("xmin");

        builder.HasIndex(p => p.ProcessCode)
            .IsUnique()
            .HasDatabaseName("ix_mfg_processes_process_code");
    }
}
