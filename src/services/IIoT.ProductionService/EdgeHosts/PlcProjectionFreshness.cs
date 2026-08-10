using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.SharedKernel.Architecture;

namespace IIoT.ProductionService.EdgeHosts;

public sealed class PlcProjectionFreshnessOptions
{
    public const string SectionName = "PlcProjectionFreshness";
    public const int DefaultWindowSeconds = 180;
    public const int MaximumWindowSeconds = 86_400;

    public int WindowSeconds { get; set; } = DefaultWindowSeconds;

    public void Validate()
    {
        if (WindowSeconds is <= 0 or > MaximumWindowSeconds)
        {
            throw new InvalidOperationException(
                $"{SectionName}:WindowSeconds must be between 1 and {MaximumWindowSeconds}.");
        }
    }
}

public enum PlcProjectionFreshnessState
{
    Current,
    Stale,
    Unavailable
}

public sealed record PlcProjectionFreshnessResolution(
    PlcProjectionFreshnessState State,
    DateTime? SnapshotReceivedAtUtc,
    TimeSpan? Age,
    bool IsCurrent,
    string? IssueCode,
    string? IssueMessage);

public interface IPlcProjectionFreshnessResolver : IReadOnlyQueryPort
{
    PlcProjectionFreshnessResolution Resolve(
        DeviceClientState? state,
        DateTime utcNow);
}

public sealed class PlcProjectionFreshnessResolver(
    PlcProjectionFreshnessOptions options)
    : IPlcProjectionFreshnessResolver
{
    public const string StaleIssueCode = "PlcSnapshotStale";
    public const string UnavailableIssueCode = "PlcSnapshotUnavailable";
    public const string ClockSkewIssueCode = "PlcSnapshotClockSkew";

    private readonly TimeSpan _window = ResolveWindow(options);

    public PlcProjectionFreshnessResolution Resolve(
        DeviceClientState? state,
        DateTime utcNow)
    {
        if (state is null
            || !state.PlcSnapshotIsAuthoritative
            || state.PlcSnapshotReceivedAtUtc is null)
        {
            return new PlcProjectionFreshnessResolution(
                PlcProjectionFreshnessState.Unavailable,
                state?.PlcSnapshotReceivedAtUtc,
                null,
                false,
                UnavailableIssueCode,
                "PLC 权威快照不可用。");
        }

        var normalizedNow = NormalizeUtc(utcNow);
        var receivedAtUtc = NormalizeUtc(
            state.PlcSnapshotReceivedAtUtc.Value);
        if (normalizedNow < receivedAtUtc)
        {
            return new PlcProjectionFreshnessResolution(
                PlcProjectionFreshnessState.Current,
                receivedAtUtc,
                TimeSpan.Zero,
                true,
                ClockSkewIssueCode,
                "PLC 快照接收时间晚于 Cloud 当前时间。");
        }

        var age = normalizedNow - receivedAtUtc;
        if (age <= _window)
        {
            return new PlcProjectionFreshnessResolution(
                PlcProjectionFreshnessState.Current,
                receivedAtUtc,
                age,
                true,
                null,
                null);
        }

        return new PlcProjectionFreshnessResolution(
            PlcProjectionFreshnessState.Stale,
            receivedAtUtc,
            age,
            false,
            StaleIssueCode,
            "PLC 权威快照已过期。");
    }

    private static TimeSpan ResolveWindow(
        PlcProjectionFreshnessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return TimeSpan.FromSeconds(options.WindowSeconds);
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
