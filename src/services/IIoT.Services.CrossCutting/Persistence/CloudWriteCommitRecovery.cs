using System.Text.Json;
using System.Text.Json.Nodes;

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
