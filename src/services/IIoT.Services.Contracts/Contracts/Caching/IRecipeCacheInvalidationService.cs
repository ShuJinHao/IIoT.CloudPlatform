namespace IIoT.Services.Contracts.Caching;

public sealed record RecipeCacheDescriptor(
    Guid RecipeId,
    Guid ProcessId,
    Guid DeviceId);

public interface IRecipeCacheInvalidationService
{
    Task InvalidateAfterChangeOnceAsync(
        Guid domainEventId,
        RecipeCacheDescriptor recipe,
        CancellationToken cancellationToken = default);
}
