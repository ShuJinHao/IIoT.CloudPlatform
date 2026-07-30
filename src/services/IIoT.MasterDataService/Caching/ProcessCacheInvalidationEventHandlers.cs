using IIoT.Core.MasterData.Aggregates.MfgProcesses.Events;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Caching;
using IIoT.Services.CrossCutting.Caching;
using MediatR;

namespace IIoT.MasterDataService.Caching;

public sealed class MfgProcessCreatedCacheInvalidationHandler(
    IIdempotentCacheInvalidationService cacheInvalidation,
    IDomainEventDispatchContext dispatchContext)
    : INotificationHandler<MfgProcessCreatedDomainEvent>
{
    public Task Handle(
        MfgProcessCreatedDomainEvent notification,
        CancellationToken cancellationToken)
        => ProcessCacheInvalidationHandler.InvalidateAsync(
            cacheInvalidation,
            dispatchContext.MessageId,
            cancellationToken);
}

public sealed class MfgProcessRenamedCacheInvalidationHandler(
    IIdempotentCacheInvalidationService cacheInvalidation,
    IDomainEventDispatchContext dispatchContext)
    : INotificationHandler<MfgProcessRenamedDomainEvent>
{
    public Task Handle(
        MfgProcessRenamedDomainEvent notification,
        CancellationToken cancellationToken)
        => ProcessCacheInvalidationHandler.InvalidateAsync(
            cacheInvalidation,
            dispatchContext.MessageId,
            cancellationToken);
}

public sealed class MfgProcessDeletedCacheInvalidationHandler(
    IIdempotentCacheInvalidationService cacheInvalidation,
    IDomainEventDispatchContext dispatchContext)
    : INotificationHandler<MfgProcessDeletedDomainEvent>
{
    public Task Handle(
        MfgProcessDeletedDomainEvent notification,
        CancellationToken cancellationToken)
        => ProcessCacheInvalidationHandler.InvalidateAsync(
            cacheInvalidation,
            dispatchContext.MessageId,
            cancellationToken);
}

internal static class ProcessCacheInvalidationHandler
{
    public static Task<bool> InvalidateAsync(
        IIdempotentCacheInvalidationService cacheInvalidation,
        Guid domainEventId,
        CancellationToken cancellationToken)
        => cacheInvalidation.InvalidateOnceAsync(
            domainEventId,
            "mfg-process-change",
            [CacheKeys.ProcessesAll()],
            [],
            cancellationToken);
}
