using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.ProductionService.EdgeHosts;

namespace IIoT.CloudPlatform.UnitTests;

public sealed class PlcProjectionFreshnessResolverTests
{
    private static readonly DateTime UtcNow =
        new(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Resolve_ShouldReturnUnavailable_WhenStateIsMissing()
    {
        var result = CreateResolver().Resolve(null, UtcNow);

        Assert.Equal(PlcProjectionFreshnessState.Unavailable, result.State);
        Assert.False(result.IsCurrent);
        Assert.Equal(
            PlcProjectionFreshnessResolver.UnavailableIssueCode,
            result.IssueCode);
    }

    [Fact]
    public void Resolve_ShouldReturnUnavailable_WhenSnapshotIsNotAuthoritative()
    {
        var state = CreateState(
            UtcNow,
            isAuthoritative: false);

        var result = CreateResolver().Resolve(state, UtcNow);

        Assert.Equal(PlcProjectionFreshnessState.Unavailable, result.State);
    }

    [Fact]
    public void Resolve_ShouldReturnUnavailable_WhenReceivedAtIsMissing()
    {
        var state = new DeviceClientState(
            Guid.NewGuid(),
            "DEV-FRESHNESS-MISSING");

        var result = CreateResolver().Resolve(state, UtcNow);

        Assert.Equal(PlcProjectionFreshnessState.Unavailable, result.State);
        Assert.Null(result.Age);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(179.999)]
    [InlineData(180)]
    public void Resolve_ShouldKeepSnapshotCurrent_AtOrInsideWindow(
        double ageSeconds)
    {
        var receivedAtUtc = UtcNow.AddSeconds(-ageSeconds);
        var state = CreateState(receivedAtUtc);

        var result = CreateResolver().Resolve(state, UtcNow);

        Assert.Equal(PlcProjectionFreshnessState.Current, result.State);
        Assert.True(result.IsCurrent);
        Assert.Equal(
            UtcNow - receivedAtUtc,
            result.Age);
    }

    [Fact]
    public void Resolve_ShouldMarkSnapshotStale_OneTickPastWindow()
    {
        var state = CreateState(
            UtcNow.AddSeconds(-180).AddTicks(-1));

        var result = CreateResolver().Resolve(state, UtcNow);

        Assert.Equal(PlcProjectionFreshnessState.Stale, result.State);
        Assert.Equal(
            PlcProjectionFreshnessResolver.StaleIssueCode,
            result.IssueCode);
    }

    [Fact]
    public void Resolve_ShouldMarkSnapshotStale_AfterWindow()
    {
        var state = CreateState(UtcNow.AddSeconds(-181));

        var result = CreateResolver().Resolve(state, UtcNow);

        Assert.Equal(PlcProjectionFreshnessState.Stale, result.State);
    }

    [Fact]
    public void Resolve_ShouldClampFutureSnapshotAgeToZero_AndDiagnoseClockSkew()
    {
        var state = CreateState(UtcNow.AddSeconds(1));

        var result = CreateResolver().Resolve(state, UtcNow);

        Assert.Equal(PlcProjectionFreshnessState.Current, result.State);
        Assert.Equal(TimeSpan.Zero, result.Age);
        Assert.Equal(
            PlcProjectionFreshnessResolver.ClockSkewIssueCode,
            result.IssueCode);
    }

    [Fact]
    public void Resolve_ShouldUseConfiguredWindow_WithSameInclusiveBoundary()
    {
        var resolver = CreateResolver(300);

        Assert.Equal(
            PlcProjectionFreshnessState.Current,
            resolver.Resolve(
                CreateState(UtcNow.AddSeconds(-300)),
                UtcNow).State);
        Assert.Equal(
            PlcProjectionFreshnessState.Stale,
            resolver.Resolve(
                CreateState(UtcNow.AddSeconds(-300).AddTicks(-1)),
                UtcNow).State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(86401)]
    public void Options_ShouldRejectInvalidWindow(int windowSeconds)
    {
        var options = new PlcProjectionFreshnessOptions
        {
            WindowSeconds = windowSeconds
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Throws<InvalidOperationException>(
            () => new PlcProjectionFreshnessResolver(options));
    }

    private static PlcProjectionFreshnessResolver CreateResolver(
        int windowSeconds =
            PlcProjectionFreshnessOptions.DefaultWindowSeconds)
        => new(new PlcProjectionFreshnessOptions
        {
            WindowSeconds = windowSeconds
        });

    private static DeviceClientState CreateState(
        DateTime receivedAtUtc,
        bool isAuthoritative = true)
    {
        var state = new DeviceClientState(
            Guid.NewGuid(),
            "DEV-FRESHNESS-01");
        state.ApplyPlcSnapshot(
            receivedAtUtc,
            receivedAtUtc,
            new string('a', 64),
            isAuthoritative);
        return state;
    }
}
