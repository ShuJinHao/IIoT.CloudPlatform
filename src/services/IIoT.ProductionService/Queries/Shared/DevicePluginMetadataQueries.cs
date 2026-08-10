using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.EdgeHosts;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Core.Production.Contracts.EdgeHosts;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.ProductionService.ClientReleases;
using IIoT.ProductionService.AiRead;
using IIoT.ProductionService.EdgeHosts;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.AiRead;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using System.Text.Json;

namespace IIoT.ProductionService.Queries.DeviceMetadata;

public sealed record DevicePlcMetadataDto(
    Guid DeviceId,
    string DeviceName,
    Guid ProcessId,
    string? PluginVersion,
    string PlcCode,
    string PlcName,
    bool IsAuthoritative,
    string? ConfigurationVersion,
    DateTime? SnapshotCapturedAtUtc,
    DateTime? SnapshotReceivedAtUtc,
    string Freshness,
    bool? Enabled,
    string? Protocol,
    string? Address,
    string? RuntimeStatus,
    bool? IsConnected,
    DateTime? LastCommunicationAtUtc,
    string? LastError);

public sealed record DeviceDataSchemaFieldDto(
    string Key,
    string Label,
    string Type,
    string? Unit,
    int? Precision,
    bool Required,
    bool IsPublic);

public sealed record DeviceDataSchemaDto(
    Guid DeviceId,
    string? PlcCode,
    string PluginVersion,
    string TypeKey,
    string DisplayName,
    string SchemaName,
    int SchemaVersion,
    string Scope,
    IReadOnlyList<string> QueryModes,
    IReadOnlyList<DeviceDataSchemaFieldDto> Fields);

[AuthorizeRequirement(EdgeHostPermissions.Read)]
public sealed record GetHumanDevicePlcsQuery(Guid DeviceId)
    : IHumanQuery<Result<IReadOnlyList<DevicePlcMetadataDto>>>;

[AuthorizeRequirement(EdgeHostPermissions.Read)]
public sealed record GetHumanDeviceDataSchemasQuery(
    Guid DeviceId,
    string? PlcCode = null)
    : IHumanQuery<Result<IReadOnlyList<DeviceDataSchemaDto>>>;

[AuthorizeAiRead(AiReadPermissions.Device)]
public sealed record GetAiReadDevicePlcsQuery(Guid DeviceId)
    : IAiReadQuery<Result<AiReadListResponse<DevicePlcMetadataDto>>>;

[AuthorizeAiRead(AiReadPermissions.ProductionRecord)]
public sealed record GetAiReadDeviceDataSchemasQuery(
    Guid DeviceId,
    string? PlcCode = null)
    : IAiReadQuery<Result<AiReadListResponse<DeviceDataSchemaDto>>>;

public sealed class GetHumanDevicePlcsHandler(
    ICurrentUserDeviceAccessService accessService,
    IReadRepository<Device> deviceRepository,
    IDevicePluginBindingQueryService bindingQueryService,
    IDeviceClientStateQueryService clientStateStore,
    IEdgeHostPlcRuntimeStateQueryService runtimeStateStore,
    IPlcProjectionFreshnessResolver plcFreshnessResolver,
    TimeProvider timeProvider)
    : IQueryHandler<
        GetHumanDevicePlcsQuery,
        Result<IReadOnlyList<DevicePlcMetadataDto>>>
{
    public async Task<Result<IReadOnlyList<DevicePlcMetadataDto>>> Handle(
        GetHumanDevicePlcsQuery request,
        CancellationToken cancellationToken)
    {
        var access = await accessService.EnsureCanAccessDeviceAsync(
            request.DeviceId,
            cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.From(access);
        }

        var resolved = await DevicePluginMetadataResolver.ResolveAsync(
            request.DeviceId,
            deviceRepository,
            bindingQueryService,
            clientStateStore,
            runtimeStateStore,
            cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        return !resolved.IsSuccess
            ? Result.From(resolved)
            : Result.Success((IReadOnlyList<DevicePlcMetadataDto>)
                DevicePluginMetadataResolver.MapPlcs(
                    resolved.Value!,
                    plcFreshnessResolver.Resolve(
                        resolved.Value!.ClientState,
                        utcNow)));
    }
}

public sealed class GetHumanDeviceDataSchemasHandler(
    ICurrentUserDeviceAccessService accessService,
    IReadRepository<Device> deviceRepository,
    IReadRepository<ClientReleaseComponent> componentRepository,
    IDevicePluginBindingQueryService bindingQueryService,
    IDeviceClientStateQueryService clientStateStore,
    IEdgeHostPlcRuntimeStateQueryService runtimeStateStore,
    IPlcProjectionFreshnessResolver plcFreshnessResolver,
    TimeProvider timeProvider)
    : IQueryHandler<
        GetHumanDeviceDataSchemasQuery,
        Result<IReadOnlyList<DeviceDataSchemaDto>>>
{
    public async Task<Result<IReadOnlyList<DeviceDataSchemaDto>>> Handle(
        GetHumanDeviceDataSchemasQuery request,
        CancellationToken cancellationToken)
    {
        var access = await accessService.EnsureCanAccessDeviceAsync(
            request.DeviceId,
            cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.From(access);
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var result = await DevicePluginMetadataResolver.ResolveSchemasAsync(
            request.DeviceId,
            request.PlcCode,
            deviceRepository,
            componentRepository,
            bindingQueryService,
            clientStateStore,
            runtimeStateStore,
            plcFreshnessResolver,
            utcNow,
            cancellationToken);
        return !result.IsSuccess
            ? Result.From(result)
            : Result.Success((IReadOnlyList<DeviceDataSchemaDto>)result.Value!);
    }
}

public sealed class GetAiReadDevicePlcsHandler(
    IAiReadScopeAccessor scopeAccessor,
    IReadRepository<Device> deviceRepository,
    IDevicePluginBindingQueryService bindingQueryService,
    IDeviceClientStateQueryService clientStateStore,
    IEdgeHostPlcRuntimeStateQueryService runtimeStateStore,
    IPlcProjectionFreshnessResolver plcFreshnessResolver,
    TimeProvider timeProvider)
    : IQueryHandler<
        GetAiReadDevicePlcsQuery,
        Result<AiReadListResponse<DevicePlcMetadataDto>>>
{
    public async Task<Result<AiReadListResponse<DevicePlcMetadataDto>>> Handle(
        GetAiReadDevicePlcsQuery request,
        CancellationToken cancellationToken)
    {
        var scope = AiReadScopeValidation.Validate(
            scopeAccessor,
            request.DeviceId);
        if (scope is not null)
        {
            return scope;
        }

        var utcNow = timeProvider.GetUtcNow();

        var resolved = await DevicePluginMetadataResolver.ResolveAsync(
            request.DeviceId,
            deviceRepository,
            bindingQueryService,
            clientStateStore,
            runtimeStateStore,
            cancellationToken);
        if (!resolved.IsSuccess)
        {
            return Result.Invalid(
                "device_plcs_unavailable: 设备 PLC 元数据不可用。");
        }

        var plcFreshness = plcFreshnessResolver.Resolve(
            resolved.Value!.ClientState,
            utcNow.UtcDateTime);
        var items = DevicePluginMetadataResolver.MapPlcs(
            resolved.Value!,
            plcFreshness);
        var unavailable = !plcFreshness.IsCurrent;
        return Result.Success(new AiReadListResponse<DevicePlcMetadataDto>(
            items,
            utcNow,
            unavailable ? "device_plcs_unavailable" : "device_plcs",
            $"deviceId={request.DeviceId:D};availability="
            + plcFreshness.State,
            items.Count,
            false));
    }
}

public sealed class GetAiReadDeviceDataSchemasHandler(
    IAiReadScopeAccessor scopeAccessor,
    IReadRepository<Device> deviceRepository,
    IReadRepository<ClientReleaseComponent> componentRepository,
    IDevicePluginBindingQueryService bindingQueryService,
    IDeviceClientStateQueryService clientStateStore,
    IEdgeHostPlcRuntimeStateQueryService runtimeStateStore,
    IPlcProjectionFreshnessResolver plcFreshnessResolver,
    TimeProvider timeProvider)
    : IQueryHandler<
        GetAiReadDeviceDataSchemasQuery,
        Result<AiReadListResponse<DeviceDataSchemaDto>>>
{
    public async Task<Result<AiReadListResponse<DeviceDataSchemaDto>>> Handle(
        GetAiReadDeviceDataSchemasQuery request,
        CancellationToken cancellationToken)
    {
        var scope = AiReadScopeValidation.Validate(
            scopeAccessor,
            request.DeviceId);
        if (scope is not null)
        {
            return scope;
        }

        var utcNow = timeProvider.GetUtcNow();

        var resolved = await DevicePluginMetadataResolver.ResolveSchemasAsync(
            request.DeviceId,
            request.PlcCode,
            deviceRepository,
            componentRepository,
            bindingQueryService,
            clientStateStore,
            runtimeStateStore,
            plcFreshnessResolver,
            utcNow.UtcDateTime,
            cancellationToken);
        if (!resolved.IsSuccess)
        {
            return Result.Invalid(
                "capability_unavailable: 设备数据能力不可用。");
        }

        var items = resolved.Value!;
        return Result.Success(new AiReadListResponse<DeviceDataSchemaDto>(
            items,
            utcNow,
            "data_schemas",
            $"deviceId={request.DeviceId:D};plcCode="
            + (string.IsNullOrWhiteSpace(request.PlcCode)
                ? "absent"
                : "present"),
            items.Count,
            false));
    }
}

internal sealed record DevicePluginMetadataContext(
    Device Device,
    DevicePluginBindingReadItem Binding,
    DeviceClientState? ClientState,
    DeviceClientVersionSnapshot? VersionSnapshot,
    string? PluginVersion,
    string? PluginPackageSha256,
    IReadOnlyList<EdgeHostPlcRuntimeState> PlcStates);

internal static class DevicePluginMetadataResolver
{
    public static async Task<Result<DevicePluginMetadataContext>> ResolveAsync(
        Guid deviceId,
        IReadRepository<Device> deviceRepository,
        IDevicePluginBindingQueryService bindingQueryService,
        IDeviceClientStateQueryService clientStateStore,
        IEdgeHostPlcRuntimeStateQueryService runtimeStateStore,
        CancellationToken cancellationToken)
    {
        if (deviceId == Guid.Empty)
        {
            return Result.Invalid("设备不能为空。");
        }

        var device = await deviceRepository.GetSingleOrDefaultAsync(
            new DeviceByIdSpec(deviceId),
            cancellationToken);
        if (device is null)
        {
            return Result.NotFound("设备不存在。");
        }

        var binding = await bindingQueryService.GetByDeviceIdAsync(
            deviceId,
            cancellationToken);
        if (binding is null)
        {
            return Result.Invalid(
                "capability_unavailable: 设备尚未建立权威插件绑定。");
        }

        var clientState = await clientStateStore.GetStateByIdentityAsync(
            device.Id,
            device.Code,
            cancellationToken);
        var versionSnapshot = await clientStateStore
            .GetVersionSnapshotByDeviceAsync(device.Id, cancellationToken);
        var installedPlugin = versionSnapshot?.InstalledPlugins
            .SingleOrDefault(plugin =>
                plugin.Enabled
                && string.Equals(
                    plugin.ModuleId,
                    binding.ModuleId,
                    StringComparison.OrdinalIgnoreCase));
        var plcStates = await runtimeStateStore.GetByIdentityAsync(
            device.Id,
            device.Code,
            cancellationToken);
        return Result.Success(new DevicePluginMetadataContext(
            device,
            binding,
            clientState,
            versionSnapshot,
            installedPlugin?.Version,
            installedPlugin?.PackageSha256,
            plcStates));
    }

    public static IReadOnlyList<DevicePlcMetadataDto> MapPlcs(
        DevicePluginMetadataContext context,
        PlcProjectionFreshnessResolution plcFreshness)
    {
        var state = context.ClientState;
        var isCurrent = plcFreshness.IsCurrent;
        return context.PlcStates
            .OrderBy(plc => plc.PlcCode, StringComparer.OrdinalIgnoreCase)
            .Select(plc => new DevicePlcMetadataDto(
                context.Device.Id,
                context.Device.DeviceName,
                context.Device.ProcessId,
                context.PluginVersion,
                plc.PlcCode,
                plc.ReportedPlcName ?? plc.PlcCode,
                state?.PlcSnapshotIsAuthoritative ?? false,
                state?.PlcSnapshotConfigurationVersion,
                state?.PlcSnapshotReportedAtUtc,
                plcFreshness.SnapshotReceivedAtUtc,
                plcFreshness.State.ToString(),
                plc.Enabled,
                plc.Protocol,
                plc.Address,
                isCurrent
                    ? plc.RuntimeStatus
                    : EdgeHostPlcRuntimeStatus.Unknown,
                isCurrent && plc.IsConnected,
                plc.LastSeenAtUtc,
                plc.LastError))
            .ToList();
    }

    public static async Task<Result<IReadOnlyList<DeviceDataSchemaDto>>>
        ResolveSchemasAsync(
            Guid deviceId,
            string? plcCode,
            IReadRepository<Device> deviceRepository,
            IReadRepository<ClientReleaseComponent> componentRepository,
            IDevicePluginBindingQueryService bindingQueryService,
            IDeviceClientStateQueryService clientStateStore,
            IEdgeHostPlcRuntimeStateQueryService runtimeStateStore,
            IPlcProjectionFreshnessResolver plcFreshnessResolver,
            DateTime utcNow,
            CancellationToken cancellationToken)
    {
        var contextResult = await ResolveAsync(
            deviceId,
            deviceRepository,
            bindingQueryService,
            clientStateStore,
            runtimeStateStore,
            cancellationToken);
        if (!contextResult.IsSuccess)
        {
            return Result.Invalid(
                "capability_unavailable: 设备插件元数据不可用。");
        }

        var context = contextResult.Value!;
        if (string.IsNullOrWhiteSpace(context.PluginVersion))
        {
            return Result.Invalid(
                "capability_unavailable: 设备尚未上报实际安装的插件版本。");
        }

        var plcFreshness = plcFreshnessResolver.Resolve(
            context.ClientState,
            utcNow);
        if (!plcFreshness.IsCurrent)
        {
            return Result.Invalid(
                "capability_unavailable: PLC 权威快照不可用或已过期。");
        }

        var normalizedPlcCode = string.IsNullOrWhiteSpace(plcCode)
            ? null
            : EdgeHostPlcRuntimeState.NormalizePlcCode(plcCode);
        if (normalizedPlcCode is not null
            && context.PlcStates.All(plc =>
                !string.Equals(
                    plc.PlcCode,
                    normalizedPlcCode,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return Result.NotFound("设备不存在指定 PLC。");
        }

        var component = await componentRepository.GetSingleOrDefaultAsync(
            new ClientReleaseComponentByComponentIdSpec(
                context.Binding.ComponentId),
            cancellationToken);
        var actualVersion = component?.FindVersion(context.PluginVersion);
        if (actualVersion is null
            || actualVersion.Status is ClientReleaseStatus.Deleted
                or ClientReleaseStatus.DeleteRequested
                or ClientReleaseStatus.DeleteFailed)
        {
            return Result.Invalid(
                "capability_unavailable: 实际安装的插件版本没有可用的发布能力清单。");
        }

        var packageEvidenceError = DevicePluginPackageEvidence.Validate(
            component!,
            actualVersion,
            context.PluginPackageSha256);
        if (packageEvidenceError is not null)
        {
            return Result.Invalid(packageEvidenceError);
        }

        IReadOnlyList<PluginDataCapability> capabilities;
        try
        {
            capabilities = PluginDataCapabilityCatalog.Parse(
                actualVersion.DataCapabilitiesJson);
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidDataException)
        {
            return Result.Invalid(
                "capability_unavailable: 实际安装插件版本的数据能力清单无效。");
        }

        var result = capabilities
            .Select(capability => new DeviceDataSchemaDto(
                context.Device.Id,
                capability.Scope.Equals(
                    "plc",
                    StringComparison.OrdinalIgnoreCase)
                    ? normalizedPlcCode
                    : null,
                actualVersion.Version,
                capability.TypeKey,
                capability.DisplayName,
                capability.SchemaName,
                capability.SchemaVersion,
                capability.Scope,
                capability.QueryModes,
                capability.Fields
                    .Select(field => new DeviceDataSchemaFieldDto(
                        field.Name,
                        field.Name,
                        field.DataType,
                        null,
                        null,
                        !field.Nullable,
                        field.IsPublic))
                    .ToList()))
            .ToList();
        return Result.Success((IReadOnlyList<DeviceDataSchemaDto>)result);
    }
}

internal static class AiReadScopeValidation
{
    public static Result? Validate(
        IAiReadScopeAccessor accessor,
        Guid deviceId)
    {
        if (deviceId == Guid.Empty)
        {
            return Result.Invalid("设备不能为空。");
        }

        return accessor.ScopeKind switch
        {
            AiReadScopeKind.Global => null,
            AiReadScopeKind.Delegated
                when accessor.DelegatedDeviceIds?.Contains(deviceId) == true
                => null,
            AiReadScopeKind.Delegated
                => Result.Forbidden(
                    "AiRead delegated device scope 不包含该设备。"),
            _ => Result.Forbidden(
                "AiRead delegated device scope 无效。")
        };
    }
}
