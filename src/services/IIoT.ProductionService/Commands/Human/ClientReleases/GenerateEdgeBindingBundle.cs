using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.ProductionService.Security;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Caching;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.ClientReleases;

/// <summary>
/// 首装绑定包：管理员在下载页为每个插件选定一台已注册设备，
/// 云端为这些设备轮换出新的启动密钥（BootstrapSecret），把“插件 -> 唯一码 + 启动密钥”
/// 一起打进绑定清单，供客户端首启写入对应 profile 机器配置后直接引导。
/// 注意：本操作会轮换所选设备的启动密钥（旧密钥随之失效），属于写操作，需管理员权限。
/// 边界：云端只产出“插件 -> 凭据”，profile/机器配置模板归客户端解析，职责分离。
/// </summary>
[AuthorizeRequirement("Device.Update")]
public sealed record GenerateEdgeBindingBundleCommand(
    IReadOnlyList<EdgeBindingSelection> Selections,
    string? BaseUrl = null) : IHumanCommand<Result<EdgeBindingBundleDto>>;

/// <summary>下载页中“一行 = 一个插件 + 一台设备”的选择项。</summary>
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

public sealed class GenerateEdgeBindingBundleHandler(
    ICurrentUser currentUser,
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IRepository<Device> deviceRepository,
    ICacheService cacheService,
    IAuditTrailService auditTrailService)
    : ICommandHandler<GenerateEdgeBindingBundleCommand, Result<EdgeBindingBundleDto>>
{
    private const int BindingSchemaVersion = 1;

    public async Task<Result<EdgeBindingBundleDto>> Handle(
        GenerateEdgeBindingBundleCommand request,
        CancellationToken cancellationToken)
    {
        // 轮换启动密钥属敏感写操作：仅管理员可执行（与设备密钥轮换一致）
        if (!currentUserDeviceAccessService.IsAdministrator)
        {
            return await FailAsync("只有管理员可以生成首装绑定包。", cancellationToken, forbidden: true);
        }

        // 1. 规范化 + 校验输入：一个插件只能绑一台设备，一台设备只能绑一个插件
        var selections = request.Selections;
        if (selections is null || selections.Count == 0)
        {
            return await FailAsync("生成绑定包失败：请至少为一个插件选择设备。", cancellationToken);
        }

        var normalized = new List<EdgeBindingSelection>(selections.Count);
        var seenModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenDevices = new HashSet<Guid>();
        foreach (var selection in selections)
        {
            var moduleId = selection.ModuleId?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(moduleId))
            {
                return await FailAsync("生成绑定包失败：存在未选择插件的配置行。", cancellationToken);
            }
            if (selection.DeviceId == Guid.Empty)
            {
                return await FailAsync($"生成绑定包失败：插件 {moduleId} 未选择设备唯一码。", cancellationToken);
            }
            if (!seenModules.Add(moduleId))
            {
                return await FailAsync($"生成绑定包失败：插件 {moduleId} 重复，请合并为一行。", cancellationToken);
            }
            if (!seenDevices.Add(selection.DeviceId))
            {
                return await FailAsync("生成绑定包失败：同一台设备不能分配给多个插件。", cancellationToken);
            }
            normalized.Add(new EdgeBindingSelection(moduleId, selection.DeviceId));
        }

        // 2. 先加载所有目标设备（任一不存在则在改动前直接失败，避免部分轮换）
        var requestedIds = normalized.Select(item => item.DeviceId).ToList();
        var devices = await deviceRepository.GetListAsync(
            new DevicePagedSpec(0, 0, requestedIds, isPaging: false),
            cancellationToken);
        var deviceById = devices.ToDictionary(device => device.Id);

        var loaded = new List<(string ModuleId, Device Device)>(normalized.Count);
        foreach (var item in normalized)
        {
            if (!deviceById.TryGetValue(item.DeviceId, out var device))
            {
                return await FailAsync(
                    $"生成绑定包失败：插件 {item.ModuleId} 选择的设备不存在或已删除。",
                    cancellationToken);
            }
            loaded.Add((item.ModuleId, device));
        }

        // 3. 逐台轮换启动密钥（生成新明文、只存哈希），统一保存
        var bindings = new List<EdgeBindingItemDto>(loaded.Count);
        foreach (var (moduleId, device) in loaded)
        {
            var bootstrapSecret = BootstrapSecretGenerator.Generate();
            device.SetBootstrapSecretHash(BootstrapSecretHasher.Hash(bootstrapSecret));
            deviceRepository.Update(device);
            bindings.Add(new EdgeBindingItemDto(
                moduleId,
                device.Code,
                bootstrapSecret,
                device.DeviceName,
                device.ProcessId));
        }

        var affected = await deviceRepository.SaveChangesAsync(cancellationToken);
        if (affected <= 0)
        {
            return await FailAsync("生成绑定包失败：保存设备启动密钥失败。", cancellationToken);
        }

        // 4. 失效设备 code 缓存（否则 bootstrap 读旧密钥哈希，新密钥验不过）+ 写审计
        foreach (var (_, device) in loaded)
        {
            await cacheService.RemoveAsync(CacheKeys.DeviceCode(device.Code), cancellationToken);
            await auditTrailService.TryWriteAsync(
                new AuditTrailEntry(
                    ParseActorUserId(currentUser.Id),
                    currentUser.UserName,
                    "Device.RotateBootstrapSecret",
                    "Device",
                    device.Id.ToString(),
                    DateTime.UtcNow,
                    true,
                    $"生成首装绑定包时轮换设备 {device.DeviceName}（{device.Code}）的启动密钥。",
                    null),
                cancellationToken);
        }

        var baseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? null : request.BaseUrl.Trim();

        return Result.Success(new EdgeBindingBundleDto(
            BindingSchemaVersion,
            baseUrl,
            DateTime.UtcNow,
            bindings));
    }

    private async Task<Result<EdgeBindingBundleDto>> FailAsync(
        string message,
        CancellationToken cancellationToken,
        bool forbidden = false)
    {
        await auditTrailService.TryWriteAsync(
            new AuditTrailEntry(
                ParseActorUserId(currentUser.Id),
                currentUser.UserName,
                "Edge.GenerateBindingBundle",
                "Device",
                "binding-bundle",
                DateTime.UtcNow,
                false,
                "生成首装绑定包。",
                message),
            cancellationToken);

        return forbidden ? Result.Forbidden(message) : Result.Failure(message);
    }

    private static Guid? ParseActorUserId(string? rawUserId)
        => Guid.TryParse(rawUserId, out var actorUserId) ? actorUserId : null;
}
