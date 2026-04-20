namespace IIoT.EntityFrameworkCore.Outbox;

public interface IOutboxMessageDispatcher
{
    Task<int> DispatchPendingAsync(CancellationToken cancellationToken = default);
}
