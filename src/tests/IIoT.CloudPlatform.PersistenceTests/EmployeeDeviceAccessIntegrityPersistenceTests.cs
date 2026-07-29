using System.Data.Common;
using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.EntityFrameworkCore.QueryServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace IIoT.CloudPlatform.PersistenceTests;

public sealed class EmployeeDeviceAccessIntegrityPersistenceTests
{
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
}
