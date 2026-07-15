using IIoT.Core.Production.Aggregates.Recipes.Events;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Caching;
using MediatR;

namespace IIoT.ProductionService.Caching;

public sealed class RecipeCreatedCacheInvalidationHandler(
    IRecipeCacheInvalidationService cacheInvalidationService,
    IDomainEventDispatchContext dispatchContext)
    : INotificationHandler<RecipeCreatedDomainEvent>
{
    public Task Handle(RecipeCreatedDomainEvent notification, CancellationToken cancellationToken) =>
        RecipeCacheInvalidationHandler.ExecuteAsync(cacheInvalidationService, dispatchContext,
            notification.RecipeId, notification.ProcessId, notification.DeviceId, cancellationToken);
}

public sealed class RecipeArchivedCacheInvalidationHandler(
    IRecipeCacheInvalidationService cacheInvalidationService,
    IDomainEventDispatchContext dispatchContext)
    : INotificationHandler<RecipeArchivedDomainEvent>
{
    public Task Handle(RecipeArchivedDomainEvent notification, CancellationToken cancellationToken) =>
        RecipeCacheInvalidationHandler.ExecuteAsync(cacheInvalidationService, dispatchContext,
            notification.RecipeId, notification.ProcessId, notification.DeviceId, cancellationToken);
}

public sealed class RecipeVersionUpgradedCacheInvalidationHandler(
    IRecipeCacheInvalidationService cacheInvalidationService,
    IDomainEventDispatchContext dispatchContext)
    : INotificationHandler<RecipeVersionUpgradedDomainEvent>
{
    public Task Handle(RecipeVersionUpgradedDomainEvent notification, CancellationToken cancellationToken) =>
        RecipeCacheInvalidationHandler.ExecuteAsync(cacheInvalidationService, dispatchContext,
            notification.SourceRecipeId, notification.ProcessId, notification.DeviceId, cancellationToken);
}

public sealed class RecipeDeletedCacheInvalidationHandler(
    IRecipeCacheInvalidationService cacheInvalidationService,
    IDomainEventDispatchContext dispatchContext)
    : INotificationHandler<RecipeDeletedDomainEvent>
{
    public Task Handle(RecipeDeletedDomainEvent notification, CancellationToken cancellationToken) =>
        RecipeCacheInvalidationHandler.ExecuteAsync(cacheInvalidationService, dispatchContext,
            notification.RecipeId, notification.ProcessId, notification.DeviceId, cancellationToken);
}

internal static class RecipeCacheInvalidationHandler
{
    public static Task ExecuteAsync(
        IRecipeCacheInvalidationService cacheInvalidationService,
        IDomainEventDispatchContext dispatchContext,
        Guid recipeId,
        Guid processId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        return cacheInvalidationService.InvalidateAfterChangeOnceAsync(
            dispatchContext.MessageId,
            new RecipeCacheDescriptor(recipeId, processId, deviceId),
            cancellationToken);
    }
}
