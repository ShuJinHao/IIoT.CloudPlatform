using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using Xunit;

namespace IIoT.CloudPlatform.UnitTests;

public sealed class DeviceClientSoftwareStatusResolverTests
{
    private static readonly DateTime UtcNow = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Resolve_ShouldReturnMissingWhenRuntimeHeartbeatDoesNotExist()
    {
        var state = new DeviceClientState(Guid.NewGuid(), "DEV-STATUS-001");

        var result = DeviceClientSoftwareStatusResolver.Resolve(state, UtcNow);

        Assert.Equal("MissingRuntimeHeartbeat", result.SoftwareStatus);
        Assert.Equal("客户端尚未上报运行心跳。", result.Issue);
    }

    [Fact]
    public void Resolve_ShouldTreatExactlyTwentyFourHoursAsFresh()
    {
        var state = CreateStateWithHeartbeat("Running", UtcNow.AddHours(-24));

        var result = DeviceClientSoftwareStatusResolver.Resolve(state, UtcNow);

        Assert.Equal("Running", result.SoftwareStatus);
        Assert.Null(result.Issue);
    }

    [Fact]
    public void Resolve_ShouldUseHeartbeatTimeEvenWhenVersionReportIsNewer()
    {
        var state = CreateStateWithHeartbeat("Running", UtcNow.AddHours(-24).AddTicks(-1));
        state.ApplyVersionReport(new DeviceClientVersionSnapshot(
            state.DeviceId,
            state.ClientCode,
            "2.0.0",
            "2.0.0",
            "stable",
            UtcNow,
            []));

        var result = DeviceClientSoftwareStatusResolver.Resolve(state, UtcNow);

        Assert.Equal("RuntimeHeartbeatStale", result.SoftwareStatus);
        Assert.Equal("超过 24 小时未收到运行心跳。", result.Issue);
    }

    [Theory]
    [InlineData("Starting", "Starting")]
    [InlineData("Running", "Running")]
    [InlineData("Stopping", "Stopped")]
    [InlineData("Stopped", "Stopped")]
    public void Resolve_ShouldMapFreshRawRuntimeStatus(string rawStatus, string expectedSoftwareStatus)
    {
        var state = CreateStateWithHeartbeat(rawStatus, UtcNow.AddMinutes(-1));

        var result = DeviceClientSoftwareStatusResolver.Resolve(state, UtcNow);

        Assert.Equal(expectedSoftwareStatus, result.SoftwareStatus);
        Assert.Null(result.Issue);
    }

    [Fact]
    public void Resolve_ShouldMapUnrecognizedPersistedRuntimeStatusToUnknown()
    {
        var state = CreateStateWithHeartbeat("Running", UtcNow.AddMinutes(-1));
        typeof(DeviceClientState)
            .GetProperty(nameof(DeviceClientState.RuntimeStatus))!
            .SetValue(state, "LegacyStatus");

        var result = DeviceClientSoftwareStatusResolver.Resolve(state, UtcNow);

        Assert.Equal("Unknown", result.SoftwareStatus);
        Assert.Null(result.Issue);
    }

    [Fact]
    public void Resolve_ShouldRejectHeartbeatBeyondFutureClockSkew()
    {
        var boundaryState = CreateStateWithHeartbeat(
            "Running",
            UtcNow.Add(DeviceClientSoftwareStatusResolver.MaximumFutureClockSkew));
        var invalidFutureState = CreateStateWithHeartbeat(
            "Running",
            UtcNow.Add(DeviceClientSoftwareStatusResolver.MaximumFutureClockSkew).AddTicks(1));

        var boundary = DeviceClientSoftwareStatusResolver.Resolve(boundaryState, UtcNow);
        var invalidFuture = DeviceClientSoftwareStatusResolver.Resolve(invalidFutureState, UtcNow);

        Assert.Equal("Running", boundary.SoftwareStatus);
        Assert.Equal("Unknown", invalidFuture.SoftwareStatus);
        Assert.Equal("运行心跳时间超出允许的未来时钟偏差。", invalidFuture.Issue);
    }

    private static DeviceClientState CreateStateWithHeartbeat(string status, DateTime reportedAtUtc)
    {
        var deviceId = Guid.NewGuid();
        const string clientCode = "DEV-STATUS-001";
        var state = new DeviceClientState(deviceId, clientCode);
        state.ApplyRuntimeHeartbeat(new EdgeDeviceRuntimeHeartbeat(
            deviceId,
            clientCode,
            "runtime-status-001",
            null,
            "1.0.0",
            "1.0.0",
            status,
            reportedAtUtc.AddHours(-1),
            reportedAtUtc));
        return state;
    }
}
