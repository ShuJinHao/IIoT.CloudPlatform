using IIoT.SharedKernel.Architecture;

namespace IIoT.Services.Contracts.RecordQueries;

public sealed record AvailableDevicePluginSeriesItem(
    Guid ComponentId,
    string ModuleId,
    string DisplayName,
    string SupportedProcessType,
    string Channel,
    string TargetRuntime,
    string? BusinessDocumentRef);

public sealed record DevicePluginBindingReadItem(
    Guid BindingId,
    Guid DeviceId,
    Guid ComponentId,
    string ModuleId,
    string DisplayName,
    string SupportedProcessType,
    string Channel,
    string TargetRuntime,
    string DataCapabilitiesJson);

public interface IDevicePluginBindingQueryService : IReadOnlyQueryPort
{
    Task<IReadOnlyList<AvailableDevicePluginSeriesItem>> GetAvailableAsync(
        string supportedProcessType,
        CancellationToken cancellationToken = default);

    Task<DevicePluginBindingReadItem?> GetByDeviceIdAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DevicePluginBindingReadItem>> GetByDeviceIdsAsync(
        IReadOnlyCollection<Guid> deviceIds,
        CancellationToken cancellationToken = default);

    Task<bool> IsComponentBoundAsync(
        Guid componentId,
        CancellationToken cancellationToken = default);
}
