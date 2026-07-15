using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.QueryServices;
using IIoT.Services.Contracts.RecordQueries;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IIoT.CloudPlatform.PersistenceTests;

public sealed class AiReadDeviceQueryServiceTests
{
    [Fact]
    public async Task GetPagedAsync_ShouldIntersectExactFiltersKeywordAndDelegatedScope()
    {
        await using var database = await SqliteEfTestDatabase.CreateAsync();
        await using var dbContext = database.CreateContext();
        var processId = Guid.NewGuid();
        var target = new Device("Assembly Device Target", "DEV-AIQUERY-001", processId);
        var wrongProcess = new Device("Assembly Device Wrong Process", "DEV-AIQUERY-002", Guid.NewGuid());
        var outsideScope = new Device("Assembly Device Outside Scope", "DEV-AIQUERY-003", processId);
        dbContext.Devices.AddRange(target, wrongProcess, outsideScope);
        await dbContext.SaveChangesAsync();
        var service = new AiReadDeviceQueryService(dbContext);

        var (items, totalCount) = await service.GetPagedAsync(
            new AiReadDeviceQueryRequest(
                target.Id,
                " dev-aiquery-001 ",
                processId,
                "Assembly",
                [target.Id, wrongProcess.Id],
                Skip: 0,
                Take: 10));

        Assert.Equal(1, totalCount);
        var item = Assert.Single(items);
        Assert.Equal(target.Id, item.Id);
        Assert.Equal("DEV-AIQUERY-001", item.DeviceCode);
    }

    [Fact]
    public async Task GetPagedAsync_ShouldUseSameFilteredSetForCountAndStablePage()
    {
        await using var database = await SqliteEfTestDatabase.CreateAsync();
        await using var dbContext = database.CreateContext();
        var processId = Guid.NewGuid();
        var devices = new[]
        {
            new Device("Line Device C", "DEV-AIQUERY-103", processId),
            new Device("Line Device A", "DEV-AIQUERY-101", processId),
            new Device("Line Device B", "DEV-AIQUERY-102", processId),
            new Device("Other Device", "DEV-AIQUERY-104", processId)
        };
        dbContext.Devices.AddRange(devices);
        await dbContext.SaveChangesAsync();
        var service = new AiReadDeviceQueryService(dbContext);

        var (items, totalCount) = await service.GetPagedAsync(
            new AiReadDeviceQueryRequest(
                DeviceId: null,
                DeviceCode: null,
                ProcessId: processId,
                Keyword: "Line",
                AllowedDeviceIds: devices.Select(device => device.Id).ToArray(),
                Skip: 0,
                Take: 2));

        Assert.Equal(3, totalCount);
        Assert.Equal(2, items.Count);
        Assert.Equal(["Line Device A", "Line Device B"], items.Select(item => item.DeviceName));
    }

    [Fact]
    public async Task GetPagedAsync_ShouldReturnEmptyForEmptyDelegatedScope()
    {
        await using var database = await SqliteEfTestDatabase.CreateAsync();
        await using var dbContext = database.CreateContext();
        dbContext.Devices.Add(new Device("Scoped Device", "DEV-AIQUERY-201", Guid.NewGuid()));
        await dbContext.SaveChangesAsync();
        var service = new AiReadDeviceQueryService(dbContext);

        var (items, totalCount) = await service.GetPagedAsync(
            new AiReadDeviceQueryRequest(null, null, null, null, [], 0, 10));

        Assert.Equal(0, totalCount);
        Assert.Empty(items);
    }

}

public sealed class AiReadProcessQueryServiceTests
{
    [Fact]
    public async Task GetPagedAsync_ShouldIntersectProcessIdAndKeyword()
    {
        await using var database = await SqliteEfTestDatabase.CreateAsync();
        await using var dbContext = database.CreateContext();
        var target = new MfgProcess("Injection", "注液工序");
        dbContext.MfgProcesses.AddRange(
            target,
            new MfgProcess("InjectionInspection", "注液检测"),
            new MfgProcess("Assembly", "装配工序"));
        await dbContext.SaveChangesAsync();
        var service = new ProcessReadQueryService(dbContext);

        var (items, totalCount) = await service.GetPagedAsync(
            target.Id,
            "注液",
            skip: 0,
            take: 10);

        Assert.Equal(1, totalCount);
        Assert.Equal(target.Id, Assert.Single(items).Id);
    }

}
