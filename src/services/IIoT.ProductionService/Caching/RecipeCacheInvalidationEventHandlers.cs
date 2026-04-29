using IIoT.Core.Production.Aggregates.Recipes.Events;
using IIoT.Services.Contracts.Caching;
using MediatR;

namespace IIoT.ProductionService.Caching;

public sealed class RecipeCreatedCacheInvalidationHandler(
    IRecipeCacheInvalidationService cacheInvalidationService)
    : INotificationHandler<RecipeCreatedDomainEvent>
{
    public Task Handle(
        RecipeCreatedDomainEvent notification,
        CancellationToken cancellationToken)
    {
        return cacheInvalidationService.InvalidateAfterChangeAsync(
            new RecipeCacheDescriptor(
                notification.RecipeId,
                notification.ProcessId,
                notification.DeviceId),
            cancellationToken);
    }
}

public sealed class RecipeArchivedCacheInvalidationHandler(
    IRecipeCacheInvalidationService cacheInvalidationService)
    : INotificationHandler<RecipeArchivedDomainEvent>
{
    public Task Handle(
        RecipeArchivedDomainEvent notification,
        CancellationToken cancellationToken)
    {
        return cacheInvalidationService.InvalidateAfterChangeAsync(
            new RecipeCacheDescriptor(
                notification.RecipeId,
                notification.ProcessId,
                notification.DeviceId),
            cancellationToken);
    }
}

public sealed class RecipeVersionUpgradedCacheInvalidationHandler(
    IRecipeCacheInvalidationService cacheInvalidationService)
    : INotificationHandler<RecipeVersionUpgradedDomainEvent>
{
    public Task Handle(
        RecipeVersionUpgradedDomainEvent notification,
        CancellationToken cancellationToken)
    {
        return cacheInvalidationService.InvalidateAfterChangeAsync(
            new RecipeCacheDescriptor(
                notification.SourceRecipeId,
                notification.ProcessId,
                notification.DeviceId),
            cancellationToken);
    }
}

public sealed class RecipeDeletedCacheInvalidationHandler(
    IRecipeCacheInvalidationService cacheInvalidationService)
    : INotificationHandler<RecipeDeletedDomainEvent>
{
    public Task Handle(
        RecipeDeletedDomainEvent notification,
        CancellationToken cancellationToken)
    {
        return cacheInvalidationService.InvalidateAfterChangeAsync(
            new RecipeCacheDescriptor(
                notification.RecipeId,
                notification.ProcessId,
                notification.DeviceId),
            cancellationToken);
    }
}
