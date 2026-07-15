using IIoT.Services.Contracts;

namespace IIoT.EntityFrameworkCore.Outbox;

public sealed class DomainEventDispatchContext : IDomainEventDispatchContext
{
    private Guid? currentMessageId;

    public Guid MessageId => currentMessageId is { } messageId && messageId != Guid.Empty
        ? messageId
        : throw new InvalidOperationException(
            "A persisted domain event outbox identity is required for this handler.");

    public IDisposable Enter(Guid messageId)
    {
        if (messageId == Guid.Empty)
            throw new ArgumentException("Domain event outbox identity cannot be empty.", nameof(messageId));
        if (currentMessageId.HasValue)
            throw new InvalidOperationException("Nested domain event dispatch contexts are not supported.");

        currentMessageId = messageId;
        return new Scope(this);
    }

    private sealed class Scope(DomainEventDispatchContext owner) : IDisposable
    {
        private DomainEventDispatchContext? currentOwner = owner;

        public void Dispose()
        {
            var captured = Interlocked.Exchange(ref currentOwner, null);
            if (captured is not null)
                captured.currentMessageId = null;
        }
    }
}
