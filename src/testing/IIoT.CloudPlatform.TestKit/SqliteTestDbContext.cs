using IIoT.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace IIoT.CloudPlatform.TestKit;

internal sealed class SqliteTestDbContext(DbContextOptions<IIoTDbContext> options)
    : IIoTDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var rowVersion = entityType.FindProperty("RowVersion");
            if (rowVersion?.ClrType != typeof(uint))
            {
                continue;
            }

            rowVersion.ValueGenerated = ValueGenerated.Never;
            rowVersion.SetBeforeSaveBehavior(PropertySaveBehavior.Save);
            rowVersion.SetAfterSaveBehavior(PropertySaveBehavior.Save);
        }
    }
}
