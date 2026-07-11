using IIoT.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace IIoT.ServiceLayer.Tests.TestInfrastructure;

internal sealed class SqliteEfTestDatabase : IAsyncDisposable
{
    private readonly string databasePath;

    private SqliteEfTestDatabase(string databasePath, DbContextOptions<IIoTDbContext> options)
    {
        this.databasePath = databasePath;
        Options = options;
    }

    public DbContextOptions<IIoTDbContext> Options { get; }

    public static async Task<SqliteEfTestDatabase> CreateAsync(params IInterceptor[] interceptors)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"iiot-ef-test-{Guid.NewGuid():N}.db");
        var builder = new DbContextOptionsBuilder<IIoTDbContext>()
            .UseSqlite($"Data Source={databasePath}");
        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        var database = new SqliteEfTestDatabase(databasePath, builder.Options);
        await using var context = database.CreateContext();
        await context.Database.EnsureCreatedAsync();
        return database;
    }

    public IIoTDbContext CreateContext() => new(Options);

    public ValueTask DisposeAsync()
    {
        DeleteIfExists(databasePath);
        DeleteIfExists($"{databasePath}-wal");
        DeleteIfExists($"{databasePath}-shm");
        return ValueTask.CompletedTask;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
