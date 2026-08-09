using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.QueryServices;

public sealed class DevicePluginBindingQueryService(IIoTDbContext dbContext)
    : IDevicePluginBindingQueryService
{
    public async Task<IReadOnlyList<AvailableDevicePluginSeriesItem>> GetAvailableAsync(
        string supportedProcessType,
        CancellationToken cancellationToken = default)
    {
        var normalized = BusinessIdentityNormalization.NormalizeClassificationCode(
            supportedProcessType,
            nameof(supportedProcessType));
        return await dbContext.ClientReleaseComponents
            .AsNoTracking()
            .Where(component =>
                component.ComponentKind == ClientReleaseComponentKind.Plugin
                && !component.WasEverDeviceBound
                && component.SupportedProcessType == normalized
                && component.Channel == "stable"
                && component.TargetRuntime == "win-x64"
                && component.Versions.Any(version =>
                    version.Status == ClientReleaseStatus.Published)
                && !dbContext.DevicePluginBindings.Any(binding =>
                    binding.ClientReleaseComponentId == component.Id))
            .OrderBy(component => component.DisplayName)
            .ThenBy(component => component.ComponentKey)
            .Select(component => new AvailableDevicePluginSeriesItem(
                component.Id,
                component.ComponentKey,
                component.DisplayName,
                component.SupportedProcessType!,
                component.Channel,
                component.TargetRuntime,
                component.BusinessDocumentRef))
            .ToListAsync(cancellationToken);
    }

    public async Task<DevicePluginBindingReadItem?> GetByDeviceIdAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
        => await Query([deviceId]).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<DevicePluginBindingReadItem>> GetByDeviceIdsAsync(
        IReadOnlyCollection<Guid> deviceIds,
        CancellationToken cancellationToken = default)
        => deviceIds.Count == 0
            ? []
            : await Query(deviceIds).ToListAsync(cancellationToken);

    public Task<bool> IsComponentBoundAsync(
        Guid componentId,
        CancellationToken cancellationToken = default)
        => dbContext.DevicePluginBindings
            .AsNoTracking()
            .AnyAsync(
                binding => binding.ClientReleaseComponentId == componentId,
                cancellationToken);

    private IQueryable<DevicePluginBindingReadItem> Query(
        IReadOnlyCollection<Guid> deviceIds)
    {
        var ids = deviceIds.ToArray();
        return from binding in dbContext.DevicePluginBindings.AsNoTracking()
               join component in dbContext.ClientReleaseComponents.AsNoTracking()
                   on binding.ClientReleaseComponentId equals component.Id
               where ids.Contains(binding.DeviceId)
               select new DevicePluginBindingReadItem(
                   binding.Id,
                   binding.DeviceId,
                   component.Id,
                   component.ComponentKey,
                   component.DisplayName,
                   binding.ProcessType,
                   component.Channel,
                   component.TargetRuntime,
                   component.DataCapabilitiesJson);
    }
}
