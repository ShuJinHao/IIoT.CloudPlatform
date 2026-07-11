using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Auditing;
using IIoT.Services.Contracts.Auditing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

namespace IIoT.ServiceLayer.Tests;

public sealed class AuditTrailPersistenceIsolationTests
{
    [Fact]
    public async Task TryWriteAsync_ShouldPersistOnlyAudit_WhenBusinessContextHasPendingAggregate()
    {
        await using var database = await SqliteAuditDatabase.CreateAsync();
        await using var businessContext = database.CreateContext();
        var pendingRelease = ClientReleaseComponent.CreateHost("audit-pending", "win-x64");
        businessContext.ClientReleaseComponents.Add(pendingRelease);
        var service = database.CreateService();

        await service.TryWriteAsync(CreateEntry("Audit.Isolated"), CancellationToken.None);

        Assert.Equal(EntityState.Added, businessContext.Entry(pendingRelease).State);
        await using var verificationContext = database.CreateContext();
        Assert.Empty(await verificationContext.ClientReleaseComponents.AsNoTracking().ToListAsync());
        var audit = Assert.Single(await verificationContext.AuditTrails.AsNoTracking().ToListAsync());
        Assert.Equal("Audit.Isolated", audit.OperationType);
    }

    [Fact]
    public async Task TryWriteAsync_ShouldNotPolluteBusinessContext_WhenAuditSaveFails()
    {
        const string sensitiveFailure = "/private/audit/SECRET-write-failure";
        var interceptor = new ThrowAuditSaveInterceptor(sensitiveFailure);
        var logger = new RecordingAuditLogger();
        await using var database = await SqliteAuditDatabase.CreateAsync(logger, interceptor);
        await using var businessContext = database.CreateContext();
        var pendingRelease = ClientReleaseComponent.CreateHost("audit-failure", "win-x64");
        businessContext.ClientReleaseComponents.Add(pendingRelease);
        var service = database.CreateService();

        await service.TryWriteAsync(CreateEntry("Audit.Failure"), CancellationToken.None);

        Assert.Equal(EntityState.Added, businessContext.Entry(pendingRelease).State);
        await businessContext.SaveChangesAsync();
        await using var verificationContext = database.CreateContext();
        Assert.Empty(await verificationContext.AuditTrails.AsNoTracking().ToListAsync());
        Assert.Single(await verificationContext.ClientReleaseComponents.AsNoTracking().ToListAsync());
        var log = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, log.Level);
        Assert.Equal(EfAuditTrailService.PersistenceFailed, log.EventId);
        Assert.Null(log.Exception);
        Assert.Contains(nameof(InvalidOperationException), log.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveFailure, log.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryWriteAsync_ShouldUseIndependentContextForEveryAudit()
    {
        var interceptor = new ContextRecordingInterceptor();
        await using var database = await SqliteAuditDatabase.CreateAsync(interceptors: interceptor);
        var service = database.CreateService();

        await service.TryWriteAsync(CreateEntry("Audit.First"), CancellationToken.None);
        await service.TryWriteAsync(CreateEntry("Audit.Second"), CancellationToken.None);

        Assert.Equal(2, interceptor.SavingContexts.Count);
        Assert.NotSame(interceptor.SavingContexts[0], interceptor.SavingContexts[1]);
        await using var verificationContext = database.CreateContext();
        Assert.Equal(2, await verificationContext.AuditTrails.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task TryWriteAsync_ShouldPropagateCallerCancellation()
    {
        await using var database = await SqliteAuditDatabase.CreateAsync();
        var service = database.CreateService();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.TryWriteAsync(CreateEntry("Audit.Cancelled"), cancellation.Token));

        await using var verificationContext = database.CreateContext();
        Assert.Empty(await verificationContext.AuditTrails.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task TryWriteAsync_ShouldPersistCompleteAuditEntry()
    {
        await using var database = await SqliteAuditDatabase.CreateAsync();
        var service = database.CreateService();
        var entry = CreateEntry("Audit.Success");

        await service.TryWriteAsync(entry, CancellationToken.None);

        await using var verificationContext = database.CreateContext();
        var record = Assert.Single(await verificationContext.AuditTrails.AsNoTracking().ToListAsync());
        Assert.Equal(entry.ActorUserId, record.ActorUserId);
        Assert.Equal(entry.ActorEmployeeNo, record.ActorEmployeeNo);
        Assert.Equal(entry.OperationType, record.OperationType);
        Assert.Equal(entry.TargetType, record.TargetType);
        Assert.Equal(entry.TargetIdOrKey, record.TargetIdOrKey);
        Assert.Equal(entry.ExecutedAtUtc, record.ExecutedAtUtc);
        Assert.Equal(entry.Succeeded, record.Succeeded);
        Assert.Equal(entry.Summary, record.Summary);
        Assert.Equal(entry.FailureReason, record.FailureReason);
    }

    private static AuditTrailEntry CreateEntry(string operationType)
    {
        return new AuditTrailEntry(
            Guid.Parse("0c7659ec-3d12-4c11-919b-c3075f20ecb2"),
            "101650",
            operationType,
            "AuditIsolationTest",
            "target-1",
            new DateTime(2026, 7, 11, 2, 30, 0, DateTimeKind.Utc),
            true,
            "Audit persistence isolation test.",
            null);
    }

    private sealed class SqliteAuditDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly RecordingAuditLogger logger;

        private SqliteAuditDatabase(
            SqliteConnection connection,
            DbContextOptions<IIoTDbContext> options,
            RecordingAuditLogger logger)
        {
            this.connection = connection;
            this.logger = logger;
            Options = options;
        }

        public DbContextOptions<IIoTDbContext> Options { get; }

        public static async Task<SqliteAuditDatabase> CreateAsync(
            RecordingAuditLogger? logger = null,
            params IInterceptor[] interceptors)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var optionsBuilder = new DbContextOptionsBuilder<IIoTDbContext>()
                .UseSqlite(connection);
            if (interceptors.Length > 0)
            {
                optionsBuilder.AddInterceptors(interceptors);
            }

            var database = new SqliteAuditDatabase(
                connection,
                optionsBuilder.Options,
                logger ?? new RecordingAuditLogger());
            await using var context = database.CreateContext();
            await context.Database.EnsureCreatedAsync();
            return database;
        }

        public IIoTDbContext CreateContext() => new(Options);

        public EfAuditTrailService CreateService() => new(Options, logger);

        public async ValueTask DisposeAsync()
        {
            await connection.DisposeAsync();
        }
    }

    private sealed class ThrowAuditSaveInterceptor(string message) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<AuditTrailRecord>().Any() == true)
            {
                throw new InvalidOperationException(message);
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class ContextRecordingInterceptor : SaveChangesInterceptor
    {
        public List<DbContext> SavingContexts { get; } = [];

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SavingContexts.Add(Assert.IsType<IIoTDbContext>(eventData.Context));
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecordingAuditLogger : ILogger<EfAuditTrailService>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        EventId EventId,
        string Message,
        Exception? Exception);
}
