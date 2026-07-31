using System.Collections.Concurrent;

namespace IIoT.EntityFrameworkCore.Identity;

public sealed class HumanSessionIssuanceProcessGate
{
    internal const int TokenExchangeQueueLimit = 8;
    internal const int AuthorizationRequestLimit = 16;
    internal const int AuthorizationPerSubjectRequestLimit = 2;
    internal const int AuthorizationDatabaseLeaseLimit = 8;

    private readonly SemaphoreSlim _tokenExchangeGate =
        new SemaphoreSlim(1, 1);
    private readonly SemaphoreSlim _tokenExchangeAdmissionSlots =
        new SemaphoreSlim(
            TokenExchangeQueueLimit + 1,
            TokenExchangeQueueLimit + 1);
    private readonly SemaphoreSlim _authorizationDatabaseLeaseSlots =
        new SemaphoreSlim(
            AuthorizationDatabaseLeaseLimit,
            AuthorizationDatabaseLeaseLimit);
    private readonly SemaphoreSlim _authorizationAdmissionSlots =
        new SemaphoreSlim(
            AuthorizationRequestLimit,
            AuthorizationRequestLimit);
    private readonly ConcurrentDictionary<Guid, AuthorizationGateEntry>
        _authorizationGates = new();
    private int _tokenExchangeWaitingCount;
    private int _authorizationDatabaseLeaseWaitingCount;

    internal int TokenExchangeWaitingCount =>
        Volatile.Read(ref _tokenExchangeWaitingCount);

    internal int AuthorizationDatabaseLeaseWaitingCount =>
        Volatile.Read(ref _authorizationDatabaseLeaseWaitingCount);

    internal int GetAuthorizationWaitingCount(Guid subjectId)
        => _authorizationGates.TryGetValue(subjectId, out var entry)
            ? entry.WaitingCount
            : 0;

    internal async ValueTask<IAsyncDisposable?> TryEnterTokenExchangeAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_tokenExchangeAdmissionSlots.Wait(0))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }

        Interlocked.Increment(ref _tokenExchangeWaitingCount);
        try
        {
            await _tokenExchangeGate.WaitAsync(cancellationToken);
        }
        catch
        {
            _tokenExchangeAdmissionSlots.Release();
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref _tokenExchangeWaitingCount);
        }

        return new TokenExchangeLease(
            _tokenExchangeGate,
            _tokenExchangeAdmissionSlots);
    }

    internal async ValueTask<IAsyncDisposable?> TryEnterAuthorizationAsync(
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AuthorizationGateEntry? entry = null;
        var referenceHeld = false;
        var subjectAdmissionHeld = false;
        var globalAdmissionHeld = false;
        var subjectGateHeld = false;
        var databaseLeaseSlotHeld = false;
        try
        {
            while (true)
            {
                entry = _authorizationGates.GetOrAdd(
                    subjectId,
                    static _ => new AuthorizationGateEntry());
                if (entry.TryAddReference())
                {
                    referenceHeld = true;
                    break;
                }

                RemoveAuthorizationEntry(subjectId, entry);
            }

            if (!entry.TryAcquireAdmission())
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReleaseAuthorizationReference(subjectId, entry);
                referenceHeld = false;
                return null;
            }

            subjectAdmissionHeld = true;
            if (!_authorizationAdmissionSlots.Wait(0))
            {
                cancellationToken.ThrowIfCancellationRequested();
                entry.ReleaseAdmission();
                subjectAdmissionHeld = false;
                ReleaseAuthorizationReference(subjectId, entry);
                referenceHeld = false;
                return null;
            }

            globalAdmissionHeld = true;
            await entry.EnterAsync(cancellationToken);
            subjectGateHeld = true;
            Interlocked.Increment(
                ref _authorizationDatabaseLeaseWaitingCount);
            try
            {
                await _authorizationDatabaseLeaseSlots.WaitAsync(
                    cancellationToken);
                databaseLeaseSlotHeld = true;
            }
            finally
            {
                Interlocked.Decrement(
                    ref _authorizationDatabaseLeaseWaitingCount);
            }

            return new AuthorizationLease(this, subjectId, entry);
        }
        catch
        {
            if (databaseLeaseSlotHeld)
            {
                _authorizationDatabaseLeaseSlots.Release();
            }

            if (subjectGateHeld)
            {
                entry!.Release();
            }

            if (globalAdmissionHeld)
            {
                _authorizationAdmissionSlots.Release();
            }

            if (subjectAdmissionHeld)
            {
                entry!.ReleaseAdmission();
            }

            if (referenceHeld)
            {
                ReleaseAuthorizationReference(subjectId, entry!);
            }

            throw;
        }
    }

    private void ReleaseAuthorization(
        Guid subjectId,
        AuthorizationGateEntry entry)
    {
        entry.Release();
        _authorizationDatabaseLeaseSlots.Release();
        _authorizationAdmissionSlots.Release();
        entry.ReleaseAdmission();
        ReleaseAuthorizationReference(subjectId, entry);
    }

    private void ReleaseAuthorizationReference(
        Guid subjectId,
        AuthorizationGateEntry entry)
    {
        if (entry.ReleaseReference())
        {
            RemoveAuthorizationEntry(subjectId, entry);
        }
    }

    private void RemoveAuthorizationEntry(
        Guid subjectId,
        AuthorizationGateEntry entry)
        => _authorizationGates.TryRemove(
            new KeyValuePair<Guid, AuthorizationGateEntry>(
                subjectId,
                entry));

    private sealed class AuthorizationGateEntry
    {
        private readonly object _referenceLock = new();
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _admissionSlots = new SemaphoreSlim(
            AuthorizationPerSubjectRequestLimit,
            AuthorizationPerSubjectRequestLimit);
        private int _referenceCount;
        private int _waitingCount;
        private bool _retired;

        public int WaitingCount => Volatile.Read(ref _waitingCount);

        public bool TryAddReference()
        {
            lock (_referenceLock)
            {
                if (_retired)
                {
                    return false;
                }

                _referenceCount++;
                return true;
            }
        }

        public bool ReleaseReference()
        {
            lock (_referenceLock)
            {
                _referenceCount--;
                if (_referenceCount != 0)
                {
                    return false;
                }

                _retired = true;
                return true;
            }
        }

        public async Task EnterAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _waitingCount);
            try
            {
                await _gate.WaitAsync(cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _waitingCount);
            }
        }

        public bool TryAcquireAdmission() => _admissionSlots.Wait(0);

        public void ReleaseAdmission() => _admissionSlots.Release();

        public void Release() => _gate.Release();
    }

    private sealed class AuthorizationLease(
        HumanSessionIssuanceProcessGate owner,
        Guid subjectId,
        AuthorizationGateEntry entry) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                owner.ReleaseAuthorization(subjectId, entry);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class TokenExchangeLease(
        SemaphoreSlim gate,
        SemaphoreSlim admissionSlots) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
                admissionSlots.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
