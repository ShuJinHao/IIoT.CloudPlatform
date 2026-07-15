using System.Collections;
using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.IdentityService.Queries;
using IIoT.MasterDataService.Queries.Processes;
using IIoT.ProductionService.Queries.Capacities;
using IIoT.ProductionService.Queries.Devices;
using IIoT.ProductionService.Queries.Recipes;
using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;
using Xunit;

namespace IIoT.CloudPlatform.ApplicationTests;

public sealed class CacheAsideQueryHandlerTests
{
    [Fact]
    public async Task GetAllDevicesHandler_AdminEmptyListIsCachedAndCallerTokenReachesRepository()
    {
        var repository = new InMemoryRepository<Device>();
        var cache = new RecordingCacheService();
        var handler = new GetAllDevicesHandler(
            new StubCurrentUserDeviceAccessService { IsAdministrator = true },
            repository,
            cache);
        using var cancellation = new CancellationTokenSource();

        await AssertSuccessfulEmptyPairAsync(() =>
            handler.Handle(new GetAllDevicesQuery(), cancellation.Token));
        Assert.Equal(2, cache.GetOrSetCalls);
        Assert.Equal(1, cache.FactoryCalls);
        Assert.Equal(1, repository.GetListCalls);
        Assert.Equal(cancellation.Token, repository.LastGetListCancellationToken);
        Assert.True(cache.Values.ContainsKey(CacheKeys.AllDevices()));
        Assert.Equal(TimeSpan.FromHours(2), cache.LastAbsoluteExpireTime);
    }

    [Fact]
    public async Task GetRecipeByIdHandler_NotFoundIsNotCachedAndValidResultIsCachedAfterPermissionCheck()
    {
        var repository = new InMemoryRepository<Recipe>();
        var cache = new RecordingCacheService();
        var access = new StubCurrentUserDeviceAccessService { IsAdministrator = true };
        var handler = new GetRecipeByIdHandler(repository, cache, access);
        var recipeId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();

        var first = await handler.Handle(new GetRecipeByIdQuery(recipeId), cancellation.Token);
        var second = await handler.Handle(new GetRecipeByIdQuery(recipeId), cancellation.Token);

        Assert.False(first.IsSuccess);
        Assert.False(second.IsSuccess);
        Assert.Equal(2, repository.GetSingleOrDefaultCalls);
        Assert.Equal(cancellation.Token, repository.LastGetSingleOrDefaultCancellationToken);
        Assert.Equal(2, cache.FactoryCalls);
        Assert.False(cache.Values.ContainsKey(CacheKeys.Recipe(recipeId)));
        Assert.Equal(0, access.EnsureCanAccessDeviceCalls);

        var recipe = new Recipe(
            "Recipe-A",
            Guid.NewGuid(),
            Guid.NewGuid(),
            "{\"speed\":120}");
        repository.SingleOrDefaultResult = recipe;

        var validFirst = await handler.Handle(
            new GetRecipeByIdQuery(recipe.Id),
            cancellation.Token);
        var validSecond = await handler.Handle(
            new GetRecipeByIdQuery(recipe.Id),
            cancellation.Token);

        Assert.True(validFirst.IsSuccess);
        Assert.Equal(recipe.Id, validFirst.Value!.Id);
        Assert.True(validSecond.IsSuccess);
        Assert.Equal(recipe.DeviceId, validSecond.Value!.DeviceId);
        Assert.Equal(3, repository.GetSingleOrDefaultCalls);
        Assert.Equal(3, cache.FactoryCalls);
        Assert.Equal(2, access.EnsureCanAccessDeviceCalls);
        Assert.Equal(cancellation.Token, access.LastEnsureCanAccessDeviceCancellationToken);
        Assert.True(cache.Values.ContainsKey(CacheKeys.Recipe(recipe.Id)));
        Assert.Equal(TimeSpan.FromHours(2), cache.LastAbsoluteExpireTime);
    }

    [Fact]
    public async Task GetRecipesByDeviceIdHandler_NonexistentDeviceIsNotCachedButValidEmptyListIsCached()
    {
        var missingDeviceId = Guid.NewGuid();
        var missingRepository = new InMemoryRepository<Recipe>();
        var missingDeviceQueries = new StubDeviceReadQueryService();
        var missingCache = new RecordingCacheService();
        var missingHandler = new GetRecipesByDeviceIdHandler(
            missingRepository,
            missingDeviceQueries,
            missingCache);
        using var cancellation = new CancellationTokenSource();

        var missingFirst = await missingHandler.Handle(
            new GetRecipesByDeviceIdQuery(missingDeviceId),
            cancellation.Token);
        var missingSecond = await missingHandler.Handle(
            new GetRecipesByDeviceIdQuery(missingDeviceId),
            cancellation.Token);

        Assert.False(missingFirst.IsSuccess);
        Assert.False(missingSecond.IsSuccess);
        Assert.Equal(2, missingDeviceQueries.ExistsCalls);
        Assert.Equal(cancellation.Token, missingDeviceQueries.LastExistsCancellationToken);
        Assert.Equal(0, missingRepository.GetListCalls);
        Assert.Equal(2, missingCache.FactoryCalls);
        Assert.False(missingCache.Values.ContainsKey(CacheKeys.RecipesByDevice(missingDeviceId)));

        var validDeviceId = Guid.NewGuid();
        var validRepository = new InMemoryRepository<Recipe>();
        var validDeviceQueries = new StubDeviceReadQueryService { Exists = true };
        var validCache = new RecordingCacheService();
        var validHandler = new GetRecipesByDeviceIdHandler(
            validRepository,
            validDeviceQueries,
            validCache);

        var validFirst = await validHandler.Handle(
            new GetRecipesByDeviceIdQuery(validDeviceId),
            CancellationToken.None);
        var validSecond = await validHandler.Handle(
            new GetRecipesByDeviceIdQuery(validDeviceId),
            CancellationToken.None);

        Assert.True(validFirst.IsSuccess);
        Assert.Empty(validFirst.Value!);
        Assert.True(validSecond.IsSuccess);
        Assert.Empty(validSecond.Value!);
        Assert.Equal(1, validDeviceQueries.ExistsCalls);
        Assert.Equal(1, validRepository.GetListCalls);
        Assert.Equal(1, validCache.FactoryCalls);
        Assert.True(validCache.Values.ContainsKey(CacheKeys.RecipesByDevice(validDeviceId)));
    }

    [Fact]
    public async Task GetEdgeSummaryByDeviceIdHandler_NullSummaryIsNotCached()
    {
        var deviceId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var queryService = new StubCapacityQueryService();
        var cache = new RecordingCacheService();
        var handler = new GetEdgeSummaryByDeviceIdHandler(queryService, cache);
        using var cancellation = new CancellationTokenSource();

        var first = await handler.Handle(
            new GetEdgeSummaryByDeviceIdQuery(deviceId, date),
            cancellation.Token);
        var second = await handler.Handle(
            new GetEdgeSummaryByDeviceIdQuery(deviceId, date),
            cancellation.Token);

        Assert.True(first.IsSuccess);
        Assert.Null(first.Value);
        Assert.True(second.IsSuccess);
        Assert.Null(second.Value);
        Assert.Equal(2, queryService.SummaryCalls);
        Assert.Equal(2, cache.FactoryCalls);
        Assert.Equal(cancellation.Token, queryService.LastSummaryCancellationToken);
        Assert.False(cache.Values.ContainsKey(CacheKeys.CapacitySummary(deviceId, date, null)));
    }

    [Fact]
    public async Task GetEdgeSummaryRangeHandler_EmptyRangeIsNotCachedAndNonEmptyRangeIsCached()
    {
        var deviceId = Guid.NewGuid();
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var end = start.AddDays(1);
        var emptyQueries = new StubCapacityQueryService();
        var emptyCache = new RecordingCacheService();
        var emptyHandler = new GetEdgeSummaryRangeHandler(emptyQueries, emptyCache);
        using var cancellation = new CancellationTokenSource();

        await AssertSuccessfulEmptyPairAsync(() => emptyHandler.Handle(
            new GetEdgeSummaryRangeQuery(deviceId, start, end),
            cancellation.Token));
        Assert.Equal(2, emptyQueries.SummaryRangeCalls);
        Assert.Equal(cancellation.Token, emptyQueries.LastSummaryRangeCancellationToken);
        Assert.Equal(2, emptyCache.FactoryCalls);
        Assert.False(emptyCache.Values.ContainsKey(CacheKeys.CapacityRange(deviceId, start, end, null)));

        var nonEmptyQueries = new StubCapacityQueryService
        {
            SummaryRangeResult = [new DailyRangeSummaryDto(start, 10, 9, 1, 6, 6, 0, 4, 3, 1)]
        };
        var nonEmptyCache = new RecordingCacheService();
        var nonEmptyHandler = new GetEdgeSummaryRangeHandler(nonEmptyQueries, nonEmptyCache);

        await nonEmptyHandler.Handle(
            new GetEdgeSummaryRangeQuery(deviceId, start, end),
            CancellationToken.None);
        await nonEmptyHandler.Handle(
            new GetEdgeSummaryRangeQuery(deviceId, start, end),
            CancellationToken.None);

        Assert.Equal(1, nonEmptyQueries.SummaryRangeCalls);
        Assert.Equal(1, nonEmptyCache.FactoryCalls);
        Assert.True(nonEmptyCache.Values.ContainsKey(CacheKeys.CapacityRange(deviceId, start, end, null)));
    }

    [Fact]
    public async Task GetDailyCapacityPagedHandler_AdminEmptyPageIsCachedAfterScopeCheck()
    {
        var pagination = new Pagination { PageNumber = 1, PageSize = 20 };
        var access = new StubCurrentUserDeviceAccessService { IsAdministrator = true };
        var queryService = new StubCapacityQueryService();
        var cache = new RecordingCacheService();
        var handler = new GetDailyCapacityPagedHandler(access, queryService, cache);
        using var cancellation = new CancellationTokenSource();

        await AssertSuccessfulEmptyPairAsync(() => handler.Handle(
            new GetDailyCapacityPagedQuery(pagination),
            cancellation.Token));
        Assert.Equal(2, access.GetAccessibleDeviceIdsCalls);
        Assert.Equal(1, queryService.DailyPagedCalls);
        Assert.Equal(1, cache.FactoryCalls);
        Assert.Equal(cancellation.Token, queryService.LastDailyPagedCancellationToken);
        Assert.True(cache.Values.ContainsKey(CacheKeys.CapacityPaged(null, null, 1, 20)));
    }

    [Fact]
    public async Task GetSummaryByDeviceIdHandler_AccessFailurePrecedesCacheAndAuthorizedNullIsNotCached()
    {
        var deviceId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var access = new StubCurrentUserDeviceAccessService
        {
            AccessibleDeviceIds = [Guid.NewGuid()]
        };
        var queryService = new StubCapacityQueryService
        {
            SummaryResult = new DailySummaryDto(10, 9, 1, 6, 6, 0, 4, 3, 1)
        };
        var cache = new RecordingCacheService();
        var handler = new GetSummaryByDeviceIdHandler(access, queryService, cache);

        var result = await handler.Handle(
            new GetSummaryByDeviceIdQuery(deviceId, date),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, access.EnsureCanAccessDeviceCalls);
        Assert.Equal(0, queryService.SummaryCalls);
        Assert.Equal(0, cache.GetOrSetCalls);

        var authorizedAccess = new StubCurrentUserDeviceAccessService
        {
            AccessibleDeviceIds = [deviceId]
        };
        var nullQueries = new StubCapacityQueryService();
        var nullCache = new RecordingCacheService();
        var authorizedHandler = new GetSummaryByDeviceIdHandler(
            authorizedAccess,
            nullQueries,
            nullCache);

        var firstNull = await authorizedHandler.Handle(
            new GetSummaryByDeviceIdQuery(deviceId, date),
            CancellationToken.None);
        var secondNull = await authorizedHandler.Handle(
            new GetSummaryByDeviceIdQuery(deviceId, date),
            CancellationToken.None);

        Assert.True(firstNull.IsSuccess);
        Assert.Null(firstNull.Value);
        Assert.True(secondNull.IsSuccess);
        Assert.Null(secondNull.Value);
        Assert.Equal(2, authorizedAccess.EnsureCanAccessDeviceCalls);
        Assert.Equal(2, nullQueries.SummaryCalls);
        Assert.Equal(2, nullCache.FactoryCalls);
        Assert.False(nullCache.Values.ContainsKey(CacheKeys.CapacitySummary(deviceId, date, null)));
    }

    [Fact]
    public async Task GetSummaryRangeHandler_AuthorizedEmptyRangeIsNotCached()
    {
        var deviceId = Guid.NewGuid();
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var end = start.AddDays(1);
        var access = new StubCurrentUserDeviceAccessService
        {
            AccessibleDeviceIds = [deviceId]
        };
        var queryService = new StubCapacityQueryService();
        var cache = new RecordingCacheService();
        var handler = new GetSummaryRangeHandler(access, queryService, cache);
        using var cancellation = new CancellationTokenSource();

        await AssertSuccessfulEmptyPairAsync(() => handler.Handle(
            new GetSummaryRangeQuery(deviceId, start, end),
            cancellation.Token));
        Assert.Equal(2, access.EnsureCanAccessDeviceCalls);
        Assert.Equal(cancellation.Token, access.LastEnsureCanAccessDeviceCancellationToken);
        Assert.Equal(2, queryService.SummaryRangeCalls);
        Assert.Equal(cancellation.Token, queryService.LastSummaryRangeCancellationToken);
        Assert.Equal(2, cache.FactoryCalls);
        Assert.False(cache.Values.ContainsKey(CacheKeys.CapacityRange(deviceId, start, end, null)));
    }

    [Fact]
    public async Task GetAllProcessesHandler_ValidEmptyListIsCached()
    {
        var repository = new InMemoryRepository<MfgProcess>();
        var cache = new RecordingCacheService();
        var handler = new GetAllProcessesHandler(repository, cache);
        using var cancellation = new CancellationTokenSource();

        await AssertSuccessfulEmptyPairAsync(() =>
            handler.Handle(new GetAllProcessesQuery(), cancellation.Token));
        Assert.Equal(1, repository.GetListCalls);
        Assert.Equal(cancellation.Token, repository.LastGetListCancellationToken);
        Assert.Equal(1, cache.FactoryCalls);
        Assert.True(cache.Values.ContainsKey(CacheKeys.ProcessesAll()));
        Assert.Equal(TimeSpan.FromHours(4), cache.LastAbsoluteExpireTime);
    }

    [Fact]
    public async Task GetAllDefinedPermissionsHandler_ResultIsCachedAndPreCancellationSkipsRoleCalls()
    {
        var roles = new StubRolePolicyService { Roles = ["Operator"] };
        var cache = new RecordingCacheService();
        var handler = new GetAllDefinedPermissionsHandler(roles, cache);

        var first = await handler.Handle(
            new GetAllDefinedPermissionsQuery(),
            CancellationToken.None);
        var second = await handler.Handle(
            new GetAllDefinedPermissionsQuery(),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.NotEmpty(first.Value!);
        Assert.True(second.IsSuccess);
        Assert.NotEmpty(second.Value!);
        Assert.Equal(1, roles.GetAllRolesCalls);
        Assert.Equal(1, roles.GetRolePermissionsCalls);
        Assert.Equal(1, cache.FactoryCalls);
        Assert.True(cache.Values.ContainsKey(CacheKeys.AllDefinedPermissions()));

        var cancelledRoles = new StubRolePolicyService { Roles = ["Operator"] };
        var cancelledCache = new RecordingCacheService();
        var cancelledHandler = new GetAllDefinedPermissionsHandler(cancelledRoles, cancelledCache);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledHandler.Handle(
            new GetAllDefinedPermissionsQuery(),
            cancellation.Token));
        Assert.Equal(0, cancelledRoles.GetAllRolesCalls);
        Assert.False(cancelledCache.Values.ContainsKey(CacheKeys.AllDefinedPermissions()));
    }

    private static async Task AssertSuccessfulEmptyPairAsync<T>(Func<Task<Result<T>>> operation)
        where T : IEnumerable
    {
        var first = await operation();
        var second = await operation();

        Assert.True(first.IsSuccess);
        Assert.Empty(first.Value!);
        Assert.True(second.IsSuccess);
        Assert.Empty(second.Value!);
    }
}
