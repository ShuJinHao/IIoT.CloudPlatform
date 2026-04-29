using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Caching;
using IIoT.Services.CrossCutting.Caching;

namespace IIoT.ProductionService.Caching;

public sealed class RecipeCacheInvalidationService(
    ICacheService cacheService) : IRecipeCacheInvalidationService
{
    public async Task InvalidateAfterChangeAsync(
        RecipeCacheDescriptor recipe,
        CancellationToken cancellationToken = default)
    {
        await cacheService.RemoveAsync(CacheKeys.Recipe(recipe.RecipeId), cancellationToken);
        await cacheService.RemoveAsync(CacheKeys.RecipesByProcess(recipe.ProcessId), cancellationToken);
        await cacheService.RemoveAsync(CacheKeys.RecipesByDevice(recipe.DeviceId), cancellationToken);
    }
}
