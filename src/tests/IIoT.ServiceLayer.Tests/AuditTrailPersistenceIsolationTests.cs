using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Auditing;
using IIoT.Services.Contracts.Auditing;
using IIoT.ServiceLayer.Tests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIoT.ServiceLayer.Tests;

public sealed class AuditTrailPersistenceIsolationTests
{
    [Fact]
    public async Task TryWriteAsync_ShouldPersistOnlyAudit_WhenBusinessContextHasPendingAggregate()
    {
        await using var database = await SqliteEfTestDatabase.CreateAsync();
        await using var businessContext = database.CreateContext();
        var pendingRelease = ClientReleaseComponent.CreateHost("audit-pending", "win-x64");
        businessContext.ClientReleaseComponents.Add(pendingRelease);
        var service = CreateService(database);

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
        await using var database = await SqliteEfTestDatabase.CreateAsync(interceptor);
        await using var businessContext = database.CreateContext();
        var pendingRelease = ClientReleaseComponent.CreateHost("audit-failure", "win-x64");
        businessContext.ClientReleaseComponents.Add(pendingRelease);
        var service = CreateService(database, logger);

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
        await using var database = await SqliteEfTestDatabase.CreateAsync(interceptor);
        var service = CreateService(database);

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
        await using var database = await SqliteEfTestDatabase.CreateAsync();
        var service = CreateService(database);
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
        await using var database = await SqliteEfTestDatabase.CreateAsync();
        var service = CreateService(database);
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

    private static EfAuditTrailService CreateService(
        SqliteEfTestDatabase database,
        ILogger<EfAuditTrailService>? logger = null)
        => new(database.Options, logger ?? NullLogger<EfAuditTrailService>.Instance);

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
