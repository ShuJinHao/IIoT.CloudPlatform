using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.QueryServices;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.RecordQueries;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IIoT.CloudPlatform.Persistence.PostgresTests;

[Collection(PostgresPersistenceIntegrationCollection.Name)]
public sealed class DeviceProcessMigrationPostgresTests(
    ClientReleaseCommitRecoveryPostgresFixture fixture)
{
    [Fact]
    public async Task Migration_ShouldAtomicallyChangeOnlyProcessAndAdvanceRowVersion()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        var seeded = await SeedDeviceAsync(budget.ConnectionString, budget.Token);

        try
        {
            DeviceProcessMigrationResult result;
            await using (var migrationContext = CreateContext(budget.ConnectionString))
            {
                result = await new EfDeviceDeletionDependencyService(
                    migrationContext).MigrateProcessAsync(
                        seeded.DeviceId,
                        seeded.SourceProcessId,
                        seeded.TargetProcessId,
                        seeded.RowVersion,
                        AuditContext(),
                        budget.Token);
            }

            Assert.True(result.Migrated);
            Assert.Equal(DeviceProcessMigrationStatus.Migrated, result.Status);
            Assert.Equal(seeded.SourceProcessId, result.SourceProcessId);
            Assert.Equal(seeded.TargetProcessId, result.TargetProcessId);
            Assert.NotNull(result.RowVersion);
            Assert.NotEqual(seeded.RowVersion, result.RowVersion!.Value);

            await using var verificationContext = CreateContext(budget.ConnectionString);
            var current = await verificationContext.Devices
                .AsNoTracking()
                .SingleAsync(device => device.Id == seeded.DeviceId, budget.Token);
            Assert.Equal(seeded.TargetProcessId, current.ProcessId);
            Assert.Equal(seeded.DeviceName, current.DeviceName);
            Assert.Equal(seeded.ClientCode, current.Code);
            Assert.Equal(seeded.BootstrapSecretHash, current.BootstrapSecretHash);
            Assert.Equal(result.RowVersion.Value, current.RowVersion);
            var audit = await verificationContext.AuditTrails
                .AsNoTracking()
                .SingleAsync(
                    record => record.OperationType == "Device.Process.Migrate"
                              && record.TargetIdOrKey == seeded.DeviceId.ToString(),
                    budget.Token);
            Assert.True(audit.Succeeded);
            Assert.InRange(audit.Summary.Length, 1, 512);
            Assert.Contains("\"DeviceProcessMigration\"", audit.Summary);
            Assert.Contains("\"preserved\"", audit.Summary);
            Assert.DoesNotContain(seeded.BootstrapSecretHash, audit.Summary);
        }
        finally
        {
            await CleanupAsync(budget.ConnectionString, seeded, budget.Token);
        }
    }

    [Fact]
    public async Task Migration_ShouldRollbackWhenProcessSemanticHistoryExists()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        var seeded = await SeedDeviceAsync(budget.ConnectionString, budget.Token);
        var recipeId = Guid.NewGuid();

        try
        {
            await using (var seedContext = CreateContext(budget.ConnectionString))
            {
                var recipe = new Recipe(
                    recipeId,
                    $"recipe-{recipeId:N}",
                    seeded.SourceProcessId,
                    seeded.DeviceId,
                    "{}");
                recipe.ClearDomainEvents();
                seedContext.Recipes.Add(recipe);
                await seedContext.SaveChangesAsync(budget.Token);
            }

            DeviceProcessMigrationResult result;
            await using (var migrationContext = CreateContext(budget.ConnectionString))
            {
                result = await new EfDeviceDeletionDependencyService(
                    migrationContext).MigrateProcessAsync(
                        seeded.DeviceId,
                        seeded.SourceProcessId,
                        seeded.TargetProcessId,
                        seeded.RowVersion,
                        AuditContext(),
                        budget.Token);
            }

            Assert.False(result.Migrated);
            Assert.Equal(DeviceProcessMigrationStatus.Blocked, result.Status);
            Assert.Equal(1, result.Impact.Recipes);
            await using var verificationContext = CreateContext(budget.ConnectionString);
            var current = await verificationContext.Devices
                .AsNoTracking()
                .SingleAsync(device => device.Id == seeded.DeviceId, budget.Token);
            Assert.Equal(seeded.SourceProcessId, current.ProcessId);
            Assert.Equal(seeded.RowVersion, current.RowVersion);
        }
        finally
        {
            await CleanupAsync(budget.ConnectionString, seeded, budget.Token);
        }
    }

    [Fact]
    public async Task Migration_ShouldWaitForConcurrentSemanticWriteThenRejectItsCommittedHistory()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        var seeded = await SeedDeviceAsync(budget.ConnectionString, budget.Token);
        var recipeId = Guid.NewGuid();
        Task<DeviceProcessMigrationResult>? migrationTask = null;
        var writerFinished = false;

        await using var writerContext = CreateContext(budget.ConnectionString);
        await using var writerTransaction = await writerContext.Database
            .BeginTransactionAsync(budget.Token);

        try
        {
            var recipe = new Recipe(
                recipeId,
                $"recipe-{recipeId:N}",
                seeded.SourceProcessId,
                seeded.DeviceId,
                "{}");
            recipe.ClearDomainEvents();
            writerContext.Recipes.Add(recipe);
            await writerContext.SaveChangesAsync(budget.Token);

            var migrationApplicationName =
                $"device-process-migration-{Guid.NewGuid():N}";
            var migrationConnectionString = new NpgsqlConnectionStringBuilder(
                budget.ConnectionString)
            {
                ApplicationName = migrationApplicationName
            }.ConnectionString;
            await using var migrationContext = CreateContext(migrationConnectionString);
            migrationTask = new EfDeviceDeletionDependencyService(
                migrationContext).MigrateProcessAsync(
                    seeded.DeviceId,
                    seeded.SourceProcessId,
                    seeded.TargetProcessId,
                    seeded.RowVersion,
                    AuditContext(),
                    budget.Token);

            await WaitForLockWaitAsync(
                budget.ConnectionString,
                migrationApplicationName,
                budget.Token);
            Assert.False(migrationTask.IsCompleted);

            await writerTransaction.CommitAsync(budget.Token);
            writerFinished = true;
            var result = await migrationTask;

            Assert.False(result.Migrated);
            Assert.Equal(DeviceProcessMigrationStatus.Blocked, result.Status);
            Assert.Equal(1, result.Impact.Recipes);
            await using var verificationContext = CreateContext(budget.ConnectionString);
            var current = await verificationContext.Devices
                .AsNoTracking()
                .SingleAsync(device => device.Id == seeded.DeviceId, budget.Token);
            Assert.Equal(seeded.SourceProcessId, current.ProcessId);
            Assert.Equal(seeded.RowVersion, current.RowVersion);
        }
        finally
        {
            if (!writerFinished)
            {
                await writerTransaction.RollbackAsync(CancellationToken.None);
            }

            if (migrationTask is not null)
            {
                try
                {
                    await migrationTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception)
                {
                    // Preserve the original assertion/timeout failure; cleanup below
                    // remains responsible for deleting any partial test state.
                }
            }

            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await CleanupAsync(budget.ConnectionString, seeded, cleanup.Token);
        }
    }

    [Fact]
    public async Task Migration_ShouldRejectStaleRowVersionWithoutChangingProcess()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        var seeded = await SeedDeviceAsync(budget.ConnectionString, budget.Token);

        try
        {
            await using (var concurrentContext = CreateContext(budget.ConnectionString))
            {
                var device = await concurrentContext.Devices.SingleAsync(
                    candidate => candidate.Id == seeded.DeviceId,
                    budget.Token);
                device.Rename($"{seeded.DeviceName}-changed");
                await concurrentContext.SaveChangesAsync(budget.Token);
            }

            await using var migrationContext = CreateContext(budget.ConnectionString);
            await Assert.ThrowsAsync<CloudWriteConflictException>(() =>
                new EfDeviceDeletionDependencyService(
                    migrationContext).MigrateProcessAsync(
                        seeded.DeviceId,
                        seeded.SourceProcessId,
                        seeded.TargetProcessId,
                        seeded.RowVersion,
                        AuditContext(),
                        budget.Token));

            await using var verificationContext = CreateContext(budget.ConnectionString);
            var current = await verificationContext.Devices
                .AsNoTracking()
                .SingleAsync(device => device.Id == seeded.DeviceId, budget.Token);
            Assert.Equal(seeded.SourceProcessId, current.ProcessId);
            Assert.NotEqual(seeded.RowVersion, current.RowVersion);
        }
        finally
        {
            await CleanupAsync(budget.ConnectionString, seeded, budget.Token);
        }
    }

    private static async Task<SeededDevice> SeedDeviceAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var unique = Guid.NewGuid().ToString("N");
        var source = new MfgProcess($"CP-{unique}"[..24], "Source process");
        var target = new MfgProcess($"AP-{unique}"[..24], "Target process");
        var device = new Device(
            $"Migration device {unique}",
            $"MIG-{unique}"[..24],
            source.Id);
        device.SetBootstrapSecretHash($"hash-{unique}");
        source.ClearDomainEvents();
        target.ClearDomainEvents();
        device.ClearDomainEvents();

        await using var context = CreateContext(connectionString);
        context.MfgProcesses.AddRange(source, target);
        context.Devices.Add(device);
        await context.SaveChangesAsync(cancellationToken);
        return new SeededDevice(
            device.Id,
            device.DeviceName,
            device.Code,
            device.BootstrapSecretHash!,
            source.Id,
            target.Id,
            device.RowVersion);
    }

    private static async Task CleanupAsync(
        string connectionString,
        SeededDevice seeded,
        CancellationToken cancellationToken)
    {
        await using var context = CreateContext(connectionString);
        await context.AuditTrails
            .Where(record => record.OperationType == "Device.Process.Migrate"
                             && record.TargetIdOrKey == seeded.DeviceId.ToString())
            .ExecuteDeleteAsync(cancellationToken);
        await context.Recipes
            .Where(recipe => recipe.DeviceId == seeded.DeviceId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.Devices
            .Where(device => device.Id == seeded.DeviceId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.MfgProcesses
            .Where(process => process.Id == seeded.SourceProcessId
                              || process.Id == seeded.TargetProcessId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task WaitForLockWaitAsync(
        string connectionString,
        string applicationName,
        CancellationToken testToken)
    {
        using var readinessTimeout =
            CancellationTokenSource.CreateLinkedTokenSource(testToken);
        readinessTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        var readinessToken = readinessTimeout.Token;
        var observerConnectionString = new NpgsqlConnectionStringBuilder(
            connectionString)
        {
            ApplicationName =
                $"device-process-migration-observer-{Guid.NewGuid():N}"
        }.ConnectionString;

        try
        {
            await using var observer = new NpgsqlConnection(observerConnectionString);
            await observer.OpenAsync(readinessToken);
            while (true)
            {
                await using var command = new NpgsqlCommand(
                    """
                    SELECT EXISTS (
                        SELECT 1
                        FROM pg_stat_activity AS activity
                        WHERE activity.application_name = @application_name
                          AND activity.state = 'active'
                          AND activity.wait_event_type = 'Lock'
                          AND EXISTS (
                              SELECT 1
                              FROM pg_locks AS waiting_lock
                              WHERE waiting_lock.pid = activity.pid
                                AND NOT waiting_lock.granted
                          )
                    )
                    """,
                    observer);
                command.Parameters.AddWithValue(
                    "application_name",
                    applicationName);
                if (await command.ExecuteScalarAsync(readinessToken) is true)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25), readinessToken);
            }
        }
        catch (OperationCanceledException) when (!testToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Device process migration '{applicationName}' did not enter a PostgreSQL lock wait within 10 seconds.");
        }
    }

    private static IIoTDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<IIoTDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new IIoTDbContext(options);
    }

    private static DeviceProcessMigrationAuditContext AuditContext()
        => new(Guid.NewGuid(), "postgres-test", DateTime.UtcNow);

    private sealed record SeededDevice(
        Guid DeviceId,
        string DeviceName,
        string ClientCode,
        string BootstrapSecretHash,
        Guid SourceProcessId,
        Guid TargetProcessId,
        uint RowVersion);
}
