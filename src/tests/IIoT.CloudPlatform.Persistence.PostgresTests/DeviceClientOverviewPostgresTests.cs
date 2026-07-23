using System.Data.Common;
using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.QueryServices;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts.RecordQueries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace IIoT.CloudPlatform.Persistence.PostgresTests;

[Collection(PostgresPersistenceIntegrationCollection.Name)]
public sealed class DeviceClientOverviewPostgresTests(
    ClientReleaseCommitRecoveryPostgresFixture fixture)
{
    [Fact]
    public async Task OverviewQuery_ShouldFilterCountSortAndPageInPostgres_UsingExactClientIdentity()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        await using var connection = new NpgsqlConnection(budget.ConnectionString);
        await connection.OpenAsync(budget.Token);
        await using var transaction = await connection.BeginTransactionAsync(budget.Token);
        var interceptor = new RecordingCommandInterceptor();
        var options = new DbContextOptionsBuilder<IIoTDbContext>()
            .UseNpgsql(connection)
            .AddInterceptors(interceptor)
            .Options;
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var utcNow = DateTime.UtcNow;

        try
        {
            var process = new MfgProcess($"C2-{suffix}", $"C2 工序 {suffix}");
            var alpha = NewDevice($"C2 A 设备 {suffix}", $"C2-A-{suffix}", process.Id);
            var beta = NewDevice($"C2 B 设备 {suffix}", $"C2-B-{suffix}", process.Id);
            var gamma = NewDevice($"C2 C 设备 {suffix}", $"C2-C-{suffix}", process.Id);
            var denied = NewDevice($"C2 X 无权设备 {suffix}", $"C2-X-{suffix}", process.Id);
            var alphaState = CreateRunningState(
                alpha,
                "2.0.0",
                "10.52.0.1",
                "203.0.113.42",
                utcNow);
            var gammaState = CreateVersionOnlyState(gamma, "3.0.0", "10.52.0.3", utcNow);
            var deniedState = CreateRunningState(
                denied,
                "9.0.0",
                "10.52.0.99",
                null,
                utcNow);
            var wrongIdentityState = CreateRunningState(
                alpha.Id,
                $"WRONG-{suffix}",
                "99.0.0",
                "10.52.99.1",
                null,
                utcNow);

            await using (var seed = new IIoTDbContext(options))
            {
                await seed.Database.UseTransactionAsync(transaction, budget.Token);
                seed.MfgProcesses.Add(process);
                seed.Devices.AddRange(alpha, beta, gamma, denied);
                seed.DeviceClientStates.AddRange(
                    alphaState,
                    gammaState,
                    deniedState,
                    wrongIdentityState);
                await seed.SaveChangesAsync(budget.Token);
            }

            interceptor.CommandTexts.Clear();
            await using var queryContext = new IIoTDbContext(options);
            await queryContext.Database.UseTransactionAsync(transaction, budget.Token);
            var service = new DeviceClientOverviewQueryService(queryContext);
            Guid[] allowedDeviceIds = [alpha.Id, beta.Id, gamma.Id];

            var firstPage = await service.SearchAsync(
                Request(allowedDeviceIds, utcNow, offset: 0, take: 2),
                budget.Token);
            var secondPage = await service.SearchAsync(
                Request(allowedDeviceIds, utcNow, offset: 2, take: 2),
                budget.Token);
            var allPageIds = firstPage.Devices
                .Concat(secondPage.Devices)
                .Select(device => device.DeviceId)
                .ToArray();

            Assert.Equal(3, firstPage.TotalCount);
            Assert.Equal(3, secondPage.TotalCount);
            Assert.Equal(3, allPageIds.Length);
            Assert.Equal(3, allPageIds.Distinct().Count());
            Assert.Equal([alpha.Id, beta.Id, gamma.Id], allPageIds);
            Assert.DoesNotContain(denied.Id, allPageIds);

            var versionSearch = await service.SearchAsync(
                Request(allowedDeviceIds, utcNow, keyword: "3.0.0"),
                budget.Token);
            Assert.Equal(gamma.Id, Assert.Single(versionSearch.Devices).DeviceId);

            var ipSearch = await service.SearchAsync(
                Request(allowedDeviceIds, utcNow, keyword: "203.0.113.42"),
                budget.Token);
            Assert.Equal(alpha.Id, Assert.Single(ipSearch.Devices).DeviceId);

            var wrongIdentitySearch = await service.SearchAsync(
                Request(allowedDeviceIds, utcNow, keyword: "99.0.0"),
                budget.Token);
            Assert.Empty(wrongIdentitySearch.Devices);
            Assert.Equal(0, wrongIdentitySearch.TotalCount);

            var statusSorted = await service.SearchAsync(
                Request(
                    allowedDeviceIds,
                    utcNow,
                    sortField: DeviceClientOverviewSortField.SoftwareStatus),
                budget.Token);
            Assert.Equal([beta.Id, gamma.Id, alpha.Id], statusSorted.Devices.Select(row => row.DeviceId));

            Assert.Contains(
                interceptor.CommandTexts,
                sql => sql.Contains("devices", StringComparison.OrdinalIgnoreCase)
                    && sql.Contains("edge_device_client_states", StringComparison.OrdinalIgnoreCase)
                    && sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase)
                    && sql.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await PostgresTestBudget.RollbackAsync(transaction);
        }
    }

    private static DeviceClientOverviewQueryRequest Request(
        IReadOnlyCollection<Guid> allowedDeviceIds,
        DateTime utcNow,
        string? keyword = null,
        int offset = 0,
        int take = 10,
        DeviceClientOverviewSortField sortField = DeviceClientOverviewSortField.DeviceName)
        => new(
            allowedDeviceIds,
            keyword,
            sortField,
            Descending: false,
            utcNow.Subtract(DeviceClientSoftwareStatusResolver.RuntimeHeartbeatStaleThreshold),
            utcNow.Add(DeviceClientSoftwareStatusResolver.MaximumFutureClockSkew),
            offset,
            take);

    private static Device NewDevice(string name, string code, Guid processId)
    {
        var device = new Device(name, code, processId);
        device.ClearDomainEvents();
        return device;
    }

    private static DeviceClientState CreateRunningState(
        Device device,
        string version,
        string localIpAddress,
        string? remoteIpAddress,
        DateTime utcNow)
        => CreateRunningState(
            device.Id,
            device.Code,
            version,
            localIpAddress,
            remoteIpAddress,
            utcNow);

    private static DeviceClientState CreateRunningState(
        Guid deviceId,
        string clientCode,
        string version,
        string localIpAddress,
        string? remoteIpAddress,
        DateTime utcNow)
    {
        var state = new DeviceClientState(deviceId, clientCode);
        state.ApplyRuntimeHeartbeat(new EdgeDeviceRuntimeHeartbeat(
            deviceId,
            clientCode,
            $"runtime-{Guid.NewGuid():N}",
            "cutting",
            version,
            "1.0.0",
            "Running",
            utcNow.AddMinutes(-10),
            utcNow.AddMinutes(-1),
            [localIpAddress],
            remoteIpAddress));
        return state;
    }

    private static DeviceClientState CreateVersionOnlyState(
        Device device,
        string version,
        string localIpAddress,
        DateTime utcNow)
    {
        var state = new DeviceClientState(device.Id, device.Code);
        state.ApplyVersionReport(new DeviceClientVersionSnapshot(
            device.Id,
            device.Code,
            version,
            "1.0.0",
            "stable",
            utcNow.AddMinutes(-2),
            [],
            [localIpAddress]));
        return state;
    }

    private sealed class RecordingCommandInterceptor : DbCommandInterceptor
    {
        public List<string> CommandTexts { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            CommandTexts.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
