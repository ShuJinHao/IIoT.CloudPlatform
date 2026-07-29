using System.Data.Common;
using System.Reflection;
using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.EntityFrameworkCore.Persistence;
using IIoT.EntityFrameworkCore.QueryServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIoT.CloudPlatform.PersistenceTests;

public sealed class EmployeeDeviceAccessIntegrityPersistenceTests
{
    [Fact]
    public async Task BatchDeviceRead_WithoutTransaction_ShouldFailBeforeQuery()
    {
        var interceptor = new DeviceBatchQueryInterceptor();
        await using var database = await SqliteEfTestDatabase.CreateAsync(interceptor);
        await using var dbContext = database.CreateContext();
        var service = new DeviceReadQueryService(dbContext);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetExistingIdsAsync([Guid.NewGuid()]));

        Assert.Equal(
            "Batch device validation requires an active transaction.",
            exception.Message);
        Assert.Equal(0, interceptor.BatchQueryCount);
    }

    [Fact]
    public async Task BatchDeviceRead_ShouldUseOneQueryAndReturnOnlyFormalDeviceIds()
    {
        var interceptor = new DeviceBatchQueryInterceptor();
        await using var database = await SqliteEfTestDatabase.CreateAsync(interceptor);
        Guid firstDeviceId;
        Guid secondDeviceId;
        await using (var seedContext = database.CreateContext())
        {
            var process = new MfgProcess(
                $"ACCESS-{Guid.NewGuid():N}",
                "Employee access integrity");
            var firstDevice = new Device(
                "Employee access device A",
                $"ACCESS-{Guid.NewGuid():N}"[..24],
                process.Id);
            var secondDevice = new Device(
                "Employee access device B",
                $"ACCESS-{Guid.NewGuid():N}"[..24],
                process.Id);
            firstDeviceId = firstDevice.Id;
            secondDeviceId = secondDevice.Id;
            seedContext.MfgProcesses.Add(process);
            seedContext.Devices.AddRange(firstDevice, secondDevice);
            await seedContext.SaveChangesAsync();
        }

        await using var queryContext = database.CreateContext();
        await using var transaction = await queryContext.Database.BeginTransactionAsync();
        var service = new DeviceReadQueryService(queryContext);
        var missingDeviceId = Guid.NewGuid();

        var existingIds = await service.GetExistingIdsAsync(
            [firstDeviceId, missingDeviceId, firstDeviceId]);
        var emptyIds = await service.GetExistingIdsAsync([]);

        Assert.Equal([firstDeviceId], existingIds);
        Assert.Empty(emptyIds);
        Assert.Equal(1, interceptor.BatchQueryCount);
        Assert.Contains(
            "FROM \"devices\"",
            Assert.Single(interceptor.BatchQueries),
            StringComparison.Ordinal);
        Assert.DoesNotContain(secondDeviceId, existingIds);
    }

    [Fact]
    public async Task EmployeeDeviceAccess_ShouldRequireFormalDeviceAndUseNoActionDelete()
    {
        await using var database = await SqliteEfTestDatabase.CreateAsync();
        Guid employeeId;
        Guid deviceId;
        Guid missingDeviceId;
        await using (var dbContext = database.CreateContext())
        {
            var employee = TestIdentityData.AddEmployeeWithIdentity(
                dbContext,
                "E-ACCESS-FK",
                "Employee access FK");
            var process = new MfgProcess(
                $"ACCESS-FK-{Guid.NewGuid():N}",
                "Employee access FK");
            var device = new Device(
                "Employee access FK device",
                $"ACCESS-FK-{Guid.NewGuid():N}"[..24],
                process.Id);
            employeeId = employee.Id;
            deviceId = device.Id;
            missingDeviceId = Guid.NewGuid();
            dbContext.MfgProcesses.Add(process);
            dbContext.Devices.Add(device);
            employee.AddDeviceAccess(deviceId);

            await dbContext.SaveChangesAsync();

            dbContext.Set<EmployeeDeviceAccess>()
                .Add(new EmployeeDeviceAccess(employee, missingDeviceId));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                () => dbContext.SaveChangesAsync());
            Assert.NotNull(exception.InnerException);
        }

        await using var verificationContext = database.CreateContext();
        var persistedAccesses = await verificationContext
            .Set<EmployeeDeviceAccess>()
            .AsNoTracking()
            .Where(access => access.EmployeeId == employeeId)
            .Select(access => access.DeviceId)
            .ToListAsync();
        var deviceForeignKey = Assert.Single(
            verificationContext.Model
                .FindEntityType(typeof(EmployeeDeviceAccess))!
                .GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(Device));

        Assert.Equal([deviceId], persistedAccesses);
        Assert.DoesNotContain(missingDeviceId, persistedAccesses);
        Assert.Equal(DeleteBehavior.NoAction, deviceForeignKey.DeleteBehavior);
    }

    [Fact]
    public async Task RollbackFailure_ShouldReleaseTransactionStateForNextAttempt()
    {
        await using var database = await SqliteEfTestDatabase.CreateAsync();
        await using var dbContext = database.CreateContext();
        var unitOfWork = new EfUnitOfWork(
            dbContext,
            NullLogger<EfUnitOfWork>.Instance);
        var failedTransaction = new ThrowingRollbackTransaction();
        var transactionField = typeof(EfUnitOfWork).GetField(
            "_transaction",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(transactionField);
        transactionField.SetValue(unitOfWork, failedTransaction);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => unitOfWork.RollbackAsync());

        Assert.Equal(
            ThrowingRollbackTransaction.FailureMessage,
            exception.Message);
        Assert.True(failedTransaction.DisposeAsyncCalled);
        Assert.Null(transactionField.GetValue(unitOfWork));

        await unitOfWork.BeginTransactionAsync();
        Assert.NotNull(dbContext.Database.CurrentTransaction);
        await unitOfWork.RollbackAsync();
    }

    private sealed class DeviceBatchQueryInterceptor : DbCommandInterceptor
    {
        public int BatchQueryCount { get; private set; }

        public List<string> BatchQueries { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(
                    "FROM \"devices\"",
                    StringComparison.Ordinal))
            {
                BatchQueryCount++;
                BatchQueries.Add(command.CommandText);
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class ThrowingRollbackTransaction : IDbContextTransaction
    {
        public const string FailureMessage = "Simulated rollback connection failure.";

        public Guid TransactionId { get; } = Guid.NewGuid();

        public bool SupportsSavepoints => false;

        public bool DisposeAsyncCalled { get; private set; }

        public void Commit() => throw new NotSupportedException();

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Rollback() => throw new InvalidOperationException(FailureMessage);

        public Task RollbackAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(FailureMessage);

        public void CreateSavepoint(string name) => throw new NotSupportedException();

        public Task CreateSavepointAsync(
            string name,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void RollbackToSavepoint(string name) => throw new NotSupportedException();

        public Task RollbackToSavepointAsync(
            string name,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void ReleaseSavepoint(string name) => throw new NotSupportedException();

        public Task ReleaseSavepointAsync(
            string name,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            DisposeAsyncCalled = true;
            return ValueTask.CompletedTask;
        }
    }
}
