using IIoT.Services.Contracts.Caching;
using IIoT.Services.CrossCutting.Caching;

namespace IIoT.ProductionService.Caching;

public sealed class RecipeCacheInvalidationService(
    IIdempotentCacheInvalidationService idempotentInvalidation) : IRecipeCacheInvalidationService
{
    public async Task InvalidateAfterChangeOnceAsync(
        Guid domainEventId,
        RecipeCacheDescriptor recipe,
        CancellationToken cancellationToken = default)
    {
        await idempotentInvalidation.InvalidateOnceAsync(
            domainEventId,
            "recipe-change",
            [
                CacheKeys.Recipe(recipe.RecipeId),
                CacheKeys.RecipesByProcess(recipe.ProcessId),
                CacheKeys.RecipesByDevice(recipe.DeviceId)
            ],
            [],
            cancellationToken);
    }
}
