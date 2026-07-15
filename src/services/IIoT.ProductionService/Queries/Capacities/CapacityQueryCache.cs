using IIoT.Services.Contracts;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Caching;

namespace IIoT.ProductionService.Queries.Capacities;

internal static class CapacityQueryCache
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public static Task<DailySummaryDto?> GetSummaryAsync(
        ICapacityQueryService queryService,
        ICacheService cacheService,
        Guid deviceId,
        DateOnly date,
        string? plcName,
        CancellationToken cancellationToken) =>
        cacheService.GetOrSetAsync(
            CacheKeys.CapacitySummary(deviceId, date, plcName),
            factoryCancellationToken => queryService.GetSummaryByDeviceIdAsync(
                deviceId,
                date,
                plcName,
                factoryCancellationToken),
            static value => value is not null,
            CacheDuration,
            cancellationToken);

    public static async Task<List<DailyRangeSummaryDto>> GetSummaryRangeAsync(
        ICapacityQueryService queryService,
        ICacheService cacheService,
        Guid deviceId,
        DateOnly startDate,
        DateOnly endDate,
        string? plcName,
        CancellationToken cancellationToken)
    {
        var data = await cacheService.GetOrSetAsync<List<DailyRangeSummaryDto>>(
            CacheKeys.CapacityRange(deviceId, startDate, endDate, plcName),
            async factoryCancellationToken =>
                (List<DailyRangeSummaryDto>?)await queryService.GetSummaryRangeAsync(
                    deviceId,
                    startDate,
                    endDate,
                    plcName,
                    factoryCancellationToken),
            static value => value is { Count: > 0 },
            CacheDuration,
            cancellationToken);

        return data
            ?? throw new InvalidOperationException("Capacity range cache factory returned null.");
    }
}
