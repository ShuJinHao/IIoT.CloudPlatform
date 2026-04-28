namespace IIoT.EntityFrameworkCore.Outbox;

public interface IOutboxMessageDispatcher
{
    Task<OutboxDispatchResult> DispatchPendingAsync(CancellationToken cancellationToken = default);
}
