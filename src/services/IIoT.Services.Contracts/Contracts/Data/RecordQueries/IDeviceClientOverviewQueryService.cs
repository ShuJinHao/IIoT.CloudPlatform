namespace IIoT.Services.Contracts.RecordQueries;

public enum DeviceClientOverviewSortField
{
    DeviceName,
    SoftwareStatus,
    CurrentVersion,
    LastRuntimeHeartbeatAtUtc
}

public sealed record DeviceClientOverviewQueryRequest(
    IReadOnlyCollection<Guid>? AllowedDeviceIds,
    string? Keyword,
    DeviceClientOverviewSortField SortField,
    bool Descending,
    DateTime RuntimeHeartbeatStaleBeforeUtc,
    DateTime RuntimeHeartbeatFutureLimitUtc,
    int Skip,
    int Take);

public sealed record DeviceClientOverviewDeviceRow(
    Guid DeviceId,
    string DeviceName,
    string ClientCode);

public sealed record DeviceClientOverviewPage(
    IReadOnlyList<DeviceClientOverviewDeviceRow> Devices,
    int TotalCount);

public interface IDeviceClientOverviewQueryService
{
    Task<DeviceClientOverviewPage> SearchAsync(
        DeviceClientOverviewQueryRequest request,
        CancellationToken cancellationToken = default);
}
