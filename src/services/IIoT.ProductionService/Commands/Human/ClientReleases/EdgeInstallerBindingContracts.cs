namespace IIoT.ProductionService.Commands.ClientReleases;

/// <summary>首装安装包中“一行 = 一个插件 + 一台设备”的选择项。</summary>
public sealed record EdgeBindingSelection(
    string ModuleId,
    Guid DeviceId);

public sealed record EdgeBindingBundleDto(
    int SchemaVersion,
    string? BaseUrl,
    DateTime GeneratedAtUtc,
    IReadOnlyList<EdgeBindingItemDto> Bindings);

public sealed record EdgeBindingItemDto(
    string ModuleId,
    string ClientCode,
    string BootstrapSecret,
    string DeviceName,
    Guid ProcessId);
