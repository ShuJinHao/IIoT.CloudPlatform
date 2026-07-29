using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Migrations;
using IIoT.EntityFrameworkCore.QueryServices;
using IIoT.Services.Contracts.RecordQueries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace IIoT.CloudPlatform.Persistence.PostgresTests;

[Collection(PostgresPersistenceIntegrationCollection.Name)]
public sealed class EmployeeDeviceAccessIntegrityPostgresTests(
    ClientReleaseCommitRecoveryPostgresFixture fixture)
{
    private const string DeviceForeignKeyName =
        "FK_employee_device_accesses_devices_device_id";

    [Fact]
    public async Task DirectOrphanInsert_ShouldBeRejectedByDeviceForeignKey()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        await using var connection = new NpgsqlConnection(budget.ConnectionString);
        await connection.OpenAsync(budget.Token);
        await using var transaction = await connection.BeginTransactionAsync(budget.Token);
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.UseTransactionAsync(transaction, budget.Token);
        var employee = TestIdentityData.AddEmployeeWithIdentity(
            dbContext,
            $"E-FK-{Guid.NewGuid():N}"[..24],
            "Employee access FK");
        employee.ClearDomainEvents();

        try
        {
            await dbContext.SaveChangesAsync(budget.Token);
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO employee_device_accesses (employee_id, device_id)
                VALUES (@employee_id, @device_id)
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue("employee_id", employee.Id);
            command.Parameters.AddWithValue("device_id", Guid.NewGuid());

            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => command.ExecuteNonQueryAsync(budget.Token));

            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
            Assert.Equal(DeviceForeignKeyName, exception.ConstraintName);
        }
        finally
        {
            await PostgresTestBudget.RollbackAsync(transaction);
        }
    }

    [Fact]
    public async Task MigrationPreflight_ShouldReportOrphanCountAndLeaveDataUntouched()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        await using var connection = new NpgsqlConnection(budget.ConnectionString);
        await connection.OpenAsync(budget.Token);
        await using var transaction = await connection.BeginTransactionAsync(budget.Token);
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.UseTransactionAsync(transaction, budget.Token);
        var employee = TestIdentityData.AddEmployeeWithIdentity(
            dbContext,
            $"E-MIG-{Guid.NewGuid():N}"[..24],
            "Migration orphan preflight");
        employee.ClearDomainEvents();
        var missingDeviceId = Guid.NewGuid();

        try
        {
            await dbContext.SaveChangesAsync(budget.Token);
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                $"""
                 ALTER TABLE employee_device_accesses
                 DROP CONSTRAINT "{DeviceForeignKeyName}";

                 INSERT INTO employee_device_accesses (employee_id, device_id)
                 VALUES ('{employee.Id}'::uuid, '{missingDeviceId}'::uuid);
                 """,
                budget.Token);
            Assert.Equal(
                1L,
                await CountAccessAsync(
                    connection,
                    transaction,
                    employee.Id,
                    missingDeviceId,
                    budget.Token));

            var migration = new AddEmployeeDeviceAccessDeviceForeignKey();
            var preflightSql = Assert.Single(
                migration.UpOperations.OfType<SqlOperation>()).Sql;
            await transaction.SaveAsync("before_employee_access_preflight", budget.Token);
            await using var preflight = new NpgsqlCommand(
                preflightSql,
                connection,
                transaction);

            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => preflight.ExecuteNonQueryAsync(budget.Token));

            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
            Assert.Contains(
                "发现 1 条孤儿设备授权",
                exception.MessageText,
                StringComparison.Ordinal);
            Assert.Contains(
                "未执行删除、补设备或数据改写",
                exception.MessageText,
                StringComparison.Ordinal);

            await transaction.RollbackAsync(
                "before_employee_access_preflight",
                budget.Token);
            Assert.Equal(
                1L,
                await CountAccessAsync(
                    connection,
                    transaction,
                    employee.Id,
                    missingDeviceId,
                    budget.Token));
        }
        finally
        {
            await PostgresTestBudget.RollbackAsync(transaction);
        }
    }

    [Fact]
    public async Task DeviceCascadeDeletion_ShouldCountAndExplicitlyDeleteEmployeeAccess()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        var unique = Guid.NewGuid().ToString("N");
        var employeeId = Guid.Empty;
        var processId = Guid.Empty;
        var deviceId = Guid.Empty;

        try
        {
            await using (var seedContext = CreateContext(budget.ConnectionString))
            {
                var employee = TestIdentityData.AddEmployeeWithIdentity(
                    seedContext,
                    $"E-DEL-{unique}"[..24],
                    "Device delete access");
                var process = new MfgProcess(
                    $"DEL-{unique}"[..24],
                    "Device delete access");
                var device = new Device(
                    $"Access device {unique}",
                    $"DEL-{unique}"[..24],
                    process.Id);
                employeeId = employee.Id;
                processId = process.Id;
                deviceId = device.Id;
                employee.AddDeviceAccess(device.Id);
                employee.ClearDomainEvents();
                device.ClearDomainEvents();
                seedContext.MfgProcesses.Add(process);
                seedContext.Devices.Add(device);
                await seedContext.SaveChangesAsync(budget.Token);
            }

            DeviceCascadeDeletionResult result;
            await using (var deleteContext = CreateContext(budget.ConnectionString))
            {
                var service = new EfDeviceDeletionDependencyService(deleteContext);
                result = await service.DeleteCascadeAsync(deviceId, budget.Token);
            }

            Assert.True(result.DeviceDeleted);
            Assert.Equal(1, result.Impact.EmployeeDeviceAccesses);
            Assert.Equal(1, result.Impact.TotalAssociatedRows);

            await using var verificationContext = CreateContext(budget.ConnectionString);
            Assert.False(await verificationContext.Devices
                .AsNoTracking()
                .AnyAsync(device => device.Id == deviceId, budget.Token));
            Assert.False(await verificationContext.Set<EmployeeDeviceAccess>()
                .AsNoTracking()
                .AnyAsync(
                    access => access.EmployeeId == employeeId
                              && access.DeviceId == deviceId,
                    budget.Token));
        }
        finally
        {
            await CleanupAsync(
                budget.ConnectionString,
                employeeId,
                processId,
                deviceId);
        }
    }

    private static IIoTDbContext CreateContext(NpgsqlConnection connection)
    {
        var options = new DbContextOptionsBuilder<IIoTDbContext>()
            .UseNpgsql(connection)
            .Options;
        return new IIoTDbContext(options);
    }

    private static IIoTDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<IIoTDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new IIoTDbContext(options);
    }

    private static async Task ExecuteNonQueryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> CountAccessAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid employeeId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*)
            FROM employee_device_accesses
            WHERE employee_id = @employee_id
              AND device_id = @device_id
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("employee_id", employeeId);
        command.Parameters.AddWithValue("device_id", deviceId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task CleanupAsync(
        string connectionString,
        Guid employeeId,
        Guid processId,
        Guid deviceId)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cleanup.Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM outbox_messages
            WHERE payload ->> 'deviceId' = @device_id_text;

            DELETE FROM employee_device_accesses
            WHERE employee_id = @employee_id OR device_id = @device_id;

            DELETE FROM devices WHERE id = @device_id;
            DELETE FROM employees WHERE id = @employee_id;
            DELETE FROM "AspNetUsers" WHERE "Id" = @employee_id;
            DELETE FROM mfg_processes WHERE id = @process_id;
            """;
        command.Parameters.AddWithValue("device_id_text", deviceId.ToString());
        command.Parameters.AddWithValue("employee_id", employeeId);
        command.Parameters.AddWithValue("device_id", deviceId);
        command.Parameters.AddWithValue("process_id", processId);
        await command.ExecuteNonQueryAsync(cleanup.Token);
    }
}
