using IIoT.Core.Production.Aggregates.ClientReleases;

namespace IIoT.ProductionService.ClientReleases;

public sealed record DeviceClientSoftwareStatusResolution(
    string SoftwareStatus,
    string? Issue);

/// <summary>
/// Resolves the client software status exclusively from the latest runtime heartbeat.
/// Version reports and aggregate update timestamps must not affect runtime freshness.
/// </summary>
public static class DeviceClientSoftwareStatusResolver
{
    public static readonly TimeSpan RuntimeHeartbeatStaleThreshold = TimeSpan.FromHours(24);
    public static readonly TimeSpan MaximumFutureClockSkew = TimeSpan.FromMinutes(5);

    public static DeviceClientSoftwareStatusResolution Resolve(
        DeviceClientState? state,
        DateTime utcNow)
    {
        if (state?.LastRuntimeHeartbeatAtUtc is null)
        {
            return new DeviceClientSoftwareStatusResolution(
                "MissingRuntimeHeartbeat",
                "客户端尚未上报运行心跳。");
        }

        var normalizedUtcNow = utcNow.ToUniversalTime();
        var lastRuntimeHeartbeatAtUtc = state.LastRuntimeHeartbeatAtUtc.Value.ToUniversalTime();
        if (lastRuntimeHeartbeatAtUtc > normalizedUtcNow.Add(MaximumFutureClockSkew))
        {
            return new DeviceClientSoftwareStatusResolution(
                "Unknown",
                "运行心跳时间超出允许的未来时钟偏差。");
        }

        if (normalizedUtcNow - lastRuntimeHeartbeatAtUtc > RuntimeHeartbeatStaleThreshold)
        {
            return new DeviceClientSoftwareStatusResolution(
                "RuntimeHeartbeatStale",
                "超过 24 小时未收到运行心跳。");
        }

        var softwareStatus = state.RuntimeStatus switch
        {
            "Starting" => "Starting",
            "Running" => "Running",
            "Stopping" or "Stopped" => "Stopped",
            _ => "Unknown"
        };
        return new DeviceClientSoftwareStatusResolution(softwareStatus, null);
    }
}
