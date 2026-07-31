using System.Text.Json;
using System.Text.Json.Nodes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;

namespace IIoT.Services.CrossCutting.Persistence;

public static class CloudWriteCommitRecovery
{
    private static readonly TimeSpan ObservationTimeout = TimeSpan.FromSeconds(5);

    public static async Task<T?> TryObserveAttemptAsync<T>(
        Func<CancellationToken, Task<T>> observeAsync,
        CancellationToken callbackCancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(observeAsync);
        callbackCancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            callbackCancellationToken);
        timeout.CancelAfter(ObservationTimeout);
        try
        {
            return await observeAsync(timeout.Token);
        }
        catch (OperationCanceledException)
            when (callbackCancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(callbackCancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<T?> TryObserveCommitAsync<T>(
        Func<CancellationToken, Task<T>> observeAsync)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(observeAsync);
        using var timeout = new CancellationTokenSource(ObservationTimeout);
        try
        {
            return await observeAsync(timeout.Token);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<CloudWriteOptionalObservation<T>?>
        TryObserveOptionalAttemptAsync<T>(
            Func<CancellationToken, Task<T?>> observeAsync,
            CancellationToken callbackCancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(observeAsync);
        callbackCancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            callbackCancellationToken);
        timeout.CancelAfter(ObservationTimeout);
        try
        {
            return new CloudWriteOptionalObservation<T>(
                await observeAsync(timeout.Token));
        }
        catch (OperationCanceledException)
            when (callbackCancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                callbackCancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<CloudWriteOptionalObservation<T>?>
        TryObserveOptionalCommitAsync<T>(
            Func<CancellationToken, Task<T?>> observeAsync)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(observeAsync);
        using var timeout = new CancellationTokenSource(
            ObservationTimeout);
        try
        {
            return new CloudWriteOptionalObservation<T>(
                await observeAsync(timeout.Token));
        }
        catch
        {
            return null;
        }
    }

    public static async Task ConfirmRecoveredAuditAsync(
        IAuditTrailService auditTrailService,
        AuditTrailEntry auditEntry)
    {
        if (!await TryConfirmRecoveredAuditAsync(
                auditTrailService,
                auditEntry))
        {
            throw new CloudWriteCommitUnknownException();
        }
    }

    public static async Task<bool> TryConfirmRecoveredAuditAsync(
        IAuditTrailService auditTrailService,
        AuditTrailEntry auditEntry)
    {
        ArgumentNullException.ThrowIfNull(auditTrailService);
        ArgumentNullException.ThrowIfNull(auditEntry);
        using var timeout = new CancellationTokenSource(ObservationTimeout);
        try
        {
            return await auditTrailService.TryWriteConfirmedAsync(
                auditEntry,
                timeout.Token);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> TryWriteRecoveredAuditAsync(
        IAuditTrailService auditTrailService,
        AuditTrailEntry auditEntry)
    {
        ArgumentNullException.ThrowIfNull(auditTrailService);
        ArgumentNullException.ThrowIfNull(auditEntry);
        using var timeout = new CancellationTokenSource(ObservationTimeout);
        try
        {
            await auditTrailService.TryWriteAsync(
                auditEntry,
                timeout.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool JsonEquals(
        string left,
        string right)
    {
        try
        {
            return JsonNode.DeepEquals(
                JsonNode.Parse(left),
                JsonNode.Parse(right));
        }
        catch (JsonException)
        {
            return string.Equals(
                left,
                right,
                StringComparison.Ordinal);
        }
    }
}

public sealed record CloudWriteOptionalObservation<T>(T? Value)
    where T : class;
