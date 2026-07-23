using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts.RecordQueries;

namespace IIoT.ProductionService.DeviceClientOverviews;

public sealed record DeviceClientOverviewItemDto(
    Guid DeviceId,
    string DeviceName,
    string? PrimaryIpAddress,
    string SoftwareStatus,
    string? CurrentVersion,
    string? Issue);

public static class DeviceClientOverviewMapping
{
    public static DeviceClientOverviewItemDto ToListItem(
        DeviceClientOverviewDeviceRow device,
        DeviceClientState? state,
        DateTime utcNow)
    {
        var softwareStatus = DeviceClientSoftwareStatusResolver.Resolve(state, utcNow);
        var runtimeLocalIpAddresses = state?.GetRuntimeLocalIpAddresses() ?? [];
        var versionLocalIpAddresses = state?.GetVersionLocalIpAddresses() ?? [];
        var primaryIpAddress = runtimeLocalIpAddresses.FirstOrDefault()
            ?? ClientReleaseText.NormalizeOptional(state?.RuntimeRemoteIpAddress)
            ?? versionLocalIpAddresses.FirstOrDefault()
            ?? ClientReleaseText.NormalizeOptional(state?.VersionRemoteIpAddress);
        var currentVersion = ClientReleaseText.NormalizeOptional(
            state?.RuntimeHostVersion ?? state?.HostVersion);

        return new DeviceClientOverviewItemDto(
            device.DeviceId,
            device.DeviceName,
            primaryIpAddress,
            softwareStatus.SoftwareStatus,
            currentVersion,
            softwareStatus.Issue);
    }
}
