namespace IIoT.ProductionService.Commands.ClientReleases;

/// <summary>首装安装包中“一行 = 一个插件 + 一台设备”的选择项。</summary>
public sealed record EdgeBindingSelection(
    string ModuleId,
    Guid DeviceId,
    string? PluginVersion = null);

public sealed record EdgeBindingBundleDto(
    int SchemaVersion,
    Guid GenerationId,
    DateTime GeneratedAtUtc,
    DateTime ExpiresAtUtc,
    string? BaseUrl,
    EdgeBindingPathsDto Paths,
    IReadOnlyList<EdgeBindingItemDto> Bindings);

public sealed record EdgeBindingPathsDto(
    string DeviceInstance,
    string BootstrapRefresh,
    string ActivateDevice,
    string ActivateDeviceConfirm,
    string IdentityDeviceLogin,
    string HumanIdentityRefresh,
    string HumanSessionValidation,
    string DeviceLog,
    string PassStationBatchTemplate,
    string CapacityHourly,
    string CapacitySummary,
    string CapacitySummaryRange,
    string RecipeByDeviceTemplate,
    string ClientReleaseCatalogTemplate,
    string ClientVersionReport,
    string RuntimeHeartbeat,
    string EdgeHostPlcRuntimeStates);

/// <summary>
/// Binding v3 的 wire schema 由 Cloud 唯一拥有。顺序同时进入计划指纹和生成字节，
/// 任何新增、删除、别名或重排都必须作为同一契约变更接受复审。
/// </summary>
public static class EdgeBindingWireSchema
{
    public const int SchemaVersion = 3;

    public static EdgeBindingPathsDto Paths { get; } = new(
        "/api/v1/edge/bootstrap/device-instance",
        "/api/v1/edge/bootstrap/edge-refresh",
        "/api/v1/edge/bootstrap/device-activate",
        "/api/v1/edge/bootstrap/device-activation-confirm",
        "/api/v1/human/identity/edge-login",
        "/api/v1/human/identity/refresh",
        "/api/v1/human/identity/session",
        "/api/v1/edge/device-logs",
        "/api/v1/edge/pass-stations/{typeKey}/batch",
        "/api/v1/edge/capacity/hourly",
        "/api/v1/edge/capacity/summary",
        "/api/v1/edge/capacity/summary/range",
        "/api/v1/edge/recipes/device/{deviceId}",
        "/api/v1/edge/client-releases/device/{deviceId}/catalog",
        "/api/v1/edge/client-releases/version-reports",
        "/api/v1/edge/runtime-heartbeats",
        "/api/v1/edge/edge-hosts/plc-runtime-states");

    public static IReadOnlyList<KeyValuePair<string, string>> OrderedRoutes { get; } =
    [
        new("deviceInstance", Paths.DeviceInstance),
        new("bootstrapRefresh", Paths.BootstrapRefresh),
        new("activateDevice", Paths.ActivateDevice),
        new("activateDeviceConfirm", Paths.ActivateDeviceConfirm),
        new("identityDeviceLogin", Paths.IdentityDeviceLogin),
        new("humanIdentityRefresh", Paths.HumanIdentityRefresh),
        new("humanSessionValidation", Paths.HumanSessionValidation),
        new("deviceLog", Paths.DeviceLog),
        new("passStationBatchTemplate", Paths.PassStationBatchTemplate),
        new("capacityHourly", Paths.CapacityHourly),
        new("capacitySummary", Paths.CapacitySummary),
        new("capacitySummaryRange", Paths.CapacitySummaryRange),
        new("recipeByDeviceTemplate", Paths.RecipeByDeviceTemplate),
        new("clientReleaseCatalogTemplate", Paths.ClientReleaseCatalogTemplate),
        new("clientVersionReport", Paths.ClientVersionReport),
        new("runtimeHeartbeat", Paths.RuntimeHeartbeat),
        new("edgeHostPlcRuntimeStates", Paths.EdgeHostPlcRuntimeStates)
    ];
}

public sealed record EdgePendingCredentialDto(
    string Name,
    string Secret);

public sealed record EdgeBindingItemDto(
    string ClientCode,
    string DeviceName,
    Guid ProcessId,
    string ProcessType,
    string ModuleId,
    string PluginVersion,
    string PackageSha256,
    string PluginDirectory,
    string ConfigDirectory,
    string DbDirectory,
    string DataDirectory,
    string LogsDirectory,
    string CacheDirectory,
    string ContextDirectory,
    string BuffersDirectory,
    EdgePendingCredentialDto PendingCredential);
