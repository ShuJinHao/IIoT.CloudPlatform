namespace IIoT.Services.Contracts.RecordQueries;

public sealed record EdgeHostOverviewDeviceRow(
    Guid DeviceId,
    string DeviceName,
    string ClientCode);

public sealed record EdgeHostOverviewPage(
    IReadOnlyList<EdgeHostOverviewDeviceRow> Devices,
    int TotalCount);

public interface IEdgeHostOverviewQueryService
{
    Task<EdgeHostOverviewPage> SearchAccessibleDevicesAsync(
        IReadOnlyCollection<Guid>? allowedDeviceIds,
        string? keyword,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
}
