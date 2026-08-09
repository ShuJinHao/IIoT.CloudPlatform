using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.Persistence;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.ClientReleases;

public sealed record ProductionBindingEvidenceItem(
    string Source,
    string Status,
    string? Value);

public sealed record ProductionBindingAnalysisDto(
    Guid DeviceId,
    string? ClientCode,
    string Status,
    bool CanApply,
    Guid? CandidateComponentId,
    string? ModuleId,
    string? PluginVersion,
    string? PackageSha256,
    string? FileManifestSha256,
    string? ProcessType,
    string? Fingerprint,
    IReadOnlyList<ProductionBindingEvidenceItem> Evidence,
    IReadOnlyList<string> Conflicts);

public interface IProductionBindingMigrationService
{
    Task<Result<ProductionBindingAnalysisDto>> AnalyzeAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);
}

public sealed class ProductionBindingMigrationService(
    IDeviceIdentityQueryService deviceIdentityQueryService,
    IDevicePluginBindingQueryService bindingQueryService,
    IDeviceClientStateQueryService clientStateQueryService,
    IEdgeInstallerGenerationStore generationStore,
    IReadRepository<ClientReleaseComponent> componentRepository)
    : IProductionBindingMigrationService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<Result<ProductionBindingAnalysisDto>> AnalyzeAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        if (deviceId == Guid.Empty)
            return Result.Invalid("设备不能为空。");

        var device = await deviceIdentityQueryService.GetByDeviceIdAsync(
            deviceId,
            cancellationToken);
        if (device is null)
            return Result.NotFound("设备不存在。");

        var existing = await bindingQueryService.GetByDeviceIdAsync(
            deviceId,
            cancellationToken);
        if (existing is not null)
        {
            return Result.Success(new ProductionBindingAnalysisDto(
                deviceId,
                device.Code,
                "AlreadyBound",
                false,
                existing.ComponentId,
                existing.ModuleId,
                null,
                null,
                null,
                existing.SupportedProcessType,
                null,
                [new("CloudBinding", "Authoritative", existing.BindingId.ToString("D"))],
                []));
        }

        var conflicts = new List<string>();
        var evidence = new List<ProductionBindingEvidenceItem>
        {
            new("CloudDevice", "Authoritative", $"{device.DeviceId:D}|{device.Code}|{device.ProcessCode}")
        };
        if (string.IsNullOrWhiteSpace(device.ProcessCode))
            conflicts.Add("Cloud 设备没有有效工序编码。");

        var heartbeat = await clientStateQueryService
            .GetRuntimeHeartbeatByIdentityAsync(
                deviceId,
                device.Code,
                cancellationToken);
        if (heartbeat is null)
        {
            conflicts.Add("缺少当前运行心跳 Profile 证据。");
            evidence.Add(new("RuntimeHeartbeat", "Missing", null));
        }
        else
        {
            var heartbeatUsable = !string.IsNullOrWhiteSpace(
                heartbeat.MachineProfile);
            if (!heartbeatUsable)
                conflicts.Add("当前运行心跳没有可核对的实际 Profile。");
            evidence.Add(new(
                "RuntimeHeartbeat",
                heartbeatUsable ? "Authoritative" : "Conflict",
                $"{heartbeat.MachineProfile}|{heartbeat.RuntimeInstanceId}|{heartbeat.LastHeartbeatAtUtc:O}"));
        }

        var snapshot = await clientStateQueryService
            .GetVersionSnapshotByDeviceAsync(deviceId, cancellationToken);
        evidence.Add(new(
            "VersionHeartbeat",
            snapshot is null ? "Missing" : "Authoritative",
            snapshot is null
                ? null
                : $"{snapshot.ReportedAtUtc:O}|{snapshot.GetContentSha256()}"));
        if (snapshot is null)
            conflicts.Add("缺少当前实际安装插件版本快照。");

        var generations = await generationStore.GetRecentByDeviceAsync(
            deviceId,
            limit: int.MaxValue,
            cancellationToken: cancellationToken);
        var candidates = new List<BindingCandidate>();
        foreach (var generation in generations)
        {
            var candidate = await TryResolveCandidateAsync(generation);
            if (candidate is null)
            {
                evidence.Add(new(
                    $"InstallerGeneration:{generation.Id:D}",
                    "Rejected",
                    $"{generation.GeneratedAtUtc:O}|{generation.Channel}|{generation.TargetRuntime}"));
                continue;
            }

            candidates.Add(candidate);
            evidence.Add(new(
                $"InstallerGeneration:{generation.Id:D}",
                "Matched",
                $"{candidate.Binding.ModuleId}|{candidate.Plugin.Version}|{candidate.Plugin.Sha256}"));
        }

        if (generations.Count == 0)
            conflicts.Add("缺少不可变安装生成记录。");
        if (candidates.Count == 0)
            conflicts.Add("没有安装记录能与 Cloud 身份、当前心跳、实际版本和已发布 manifest 同时一致。");
        else if (candidates.Count > 1)
            conflicts.Add("有多条安装记录同时符合当前证据，候选不唯一，禁止自动选择。");

        var selected = candidates.Count == 1 ? candidates[0] : null;
        var component = selected?.Component;
        var version = selected?.Version;
        var bindingFact = selected?.Binding;
        var pluginFact = selected?.Plugin;
        var generationRecord = selected?.Generation;
        var canApply = conflicts.Count == 0 && selected is not null;
        string? fingerprint = null;
        if (canApply)
        {
            var canonical = string.Join('|',
                device.DeviceId.ToString("D"),
                device.Code,
                device.ProcessCode,
                generationRecord!.Id.ToString("D"),
                component!.Id.ToString("D"),
                bindingFact!.ModuleId,
                pluginFact!.Version,
                pluginFact.Sha256,
                version!.FileManifestSha256,
                heartbeat!.MachineProfile,
                heartbeat.RuntimeInstanceId,
                heartbeat.LastHeartbeatAtUtc.ToUniversalTime().Ticks,
                snapshot!.GetContentSha256());
            fingerprint = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
                .ToLowerInvariant();
        }

        return Result.Success(new ProductionBindingAnalysisDto(
            deviceId,
            device.Code,
            canApply ? "Ready" : "Conflict",
            canApply,
            component?.Id,
            bindingFact?.ModuleId,
            pluginFact?.Version,
            pluginFact?.Sha256,
            version?.FileManifestSha256,
            device.ProcessCode,
            fingerprint,
            evidence,
            conflicts));

        async Task<BindingCandidate?> TryResolveCandidateAsync(
            EdgeInstallerGenerationRecord generation)
        {
            EdgeInstallerGenerationBindingFact[] bindingFacts;
            EdgeInstallerGenerationPluginFact[] pluginFacts;
            try
            {
                bindingFacts = JsonSerializer.Deserialize<
                    EdgeInstallerGenerationBindingFact[]>(
                    generation.BindingsJson,
                    JsonOptions) ?? [];
                pluginFacts = JsonSerializer.Deserialize<
                    EdgeInstallerGenerationPluginFact[]>(
                    generation.PluginsJson,
                    JsonOptions) ?? [];
            }
            catch (JsonException)
            {
                return null;
            }

            var matchingBindings = bindingFacts.Where(fact =>
                    fact.DeviceId == deviceId
                    && fact.ProcessId == device.ProcessId
                    && string.Equals(
                        fact.ClientCode,
                        device.Code,
                        StringComparison.Ordinal))
                .ToArray();
            if (matchingBindings.Length != 1)
                return null;
            var binding = matchingBindings[0];

            var matchingPlugins = pluginFacts.Where(fact =>
                    string.Equals(
                        fact.ModuleId,
                        binding.ModuleId,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matchingPlugins.Length != 1)
                return null;
            var plugin = matchingPlugins[0];

            if (heartbeat is null
                || string.IsNullOrWhiteSpace(heartbeat.MachineProfile)
                || (!string.IsNullOrWhiteSpace(binding.ActualProfile)
                    && (!string.Equals(
                            binding.ActualProfile,
                            device.Code,
                            StringComparison.Ordinal)
                        || !string.Equals(
                            binding.ActualProfile,
                            heartbeat.MachineProfile.Trim(),
                            StringComparison.Ordinal))))
            {
                return null;
            }

            if (snapshot is null)
                return null;
            var installedMatches = snapshot.InstalledPlugins.Where(installed =>
                    installed.Enabled
                    && string.Equals(
                        installed.ModuleId,
                        binding.ModuleId,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        installed.Version,
                        plugin.Version,
                        StringComparison.Ordinal)
                    && string.Equals(
                        installed.PackageSha256,
                        plugin.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (installedMatches.Length != 1)
                return null;

            var candidateComponent = await componentRepository
                .GetSingleOrDefaultAsync(
                    new ClientReleaseComponentByIdentitySpec(
                        ClientReleaseComponentKind.Plugin,
                        binding.ModuleId,
                        generation.Channel,
                        generation.TargetRuntime),
                    cancellationToken);
            var candidateVersion = candidateComponent?.FindVersion(
                plugin.Version);
            if (candidateComponent is null
                || candidateVersion is null
                || candidateComponent.WasEverDeviceBound
                || candidateVersion.Status != ClientReleaseStatus.Published
                || string.IsNullOrWhiteSpace(
                    candidateVersion.FileManifestSha256)
                || !string.Equals(
                    candidateVersion.Sha256,
                    plugin.Sha256,
                    StringComparison.Ordinal)
                || !string.Equals(
                    candidateComponent.SupportedProcessType,
                    device.ProcessCode,
                    StringComparison.Ordinal)
                || await bindingQueryService.IsComponentBoundAsync(
                    candidateComponent.Id,
                    cancellationToken))
            {
                return null;
            }

            return new BindingCandidate(
                generation,
                binding,
                plugin,
                candidateComponent,
                candidateVersion);
        }
    }

    private sealed record BindingCandidate(
        EdgeInstallerGenerationRecord Generation,
        EdgeInstallerGenerationBindingFact Binding,
        EdgeInstallerGenerationPluginFact Plugin,
        ClientReleaseComponent Component,
        ClientReleaseVersion Version);
}

[AuthorizeRequirement("Device.Read")]
[AdminOnly]
public sealed record AnalyzeProductionBindingQuery(Guid DeviceId)
    : IHumanQuery<Result<ProductionBindingAnalysisDto>>;

public sealed class AnalyzeProductionBindingHandler(
    IProductionBindingMigrationService service)
    : IQueryHandler<AnalyzeProductionBindingQuery, Result<ProductionBindingAnalysisDto>>
{
    public Task<Result<ProductionBindingAnalysisDto>> Handle(
        AnalyzeProductionBindingQuery request,
        CancellationToken cancellationToken)
        => service.AnalyzeAsync(request.DeviceId, cancellationToken);
}

[AuthorizeRequirement("Device.Update")]
[AdminOnly]
[DistributedLock("iiot:lock:production-binding:{DeviceId}", TimeoutSeconds = 5)]
public sealed record ApplyProductionBindingCommand(
    Guid DeviceId,
    string Fingerprint)
    : IHumanCommand<Result<ProductionBindingAnalysisDto>>;

public sealed class ApplyProductionBindingHandler(
    ICurrentUser currentUser,
    ICurrentUserDeviceAccessService deviceAccessService,
    IProductionBindingMigrationService migrationService,
    IDevicePluginBindingQueryService bindingQueryService,
    IProductionBindingApplyStore applyStore)
    : ICommandHandler<ApplyProductionBindingCommand, Result<ProductionBindingAnalysisDto>>
{
    public async Task<Result<ProductionBindingAnalysisDto>> Handle(
        ApplyProductionBindingCommand request,
        CancellationToken cancellationToken)
    {
        if (!deviceAccessService.IsAdministrator)
            return Result.Forbidden("只有管理员可以显式应用生产绑定。");
        if (string.IsNullOrWhiteSpace(request.Fingerprint))
            return Result.Invalid("分析指纹不能为空。");

        var analysisResult = await migrationService.AnalyzeAsync(
            request.DeviceId,
            cancellationToken);
        if (!analysisResult.IsSuccess)
            return Result.From(analysisResult);
        var analysis = analysisResult.Value!;
        if (!analysis.CanApply
            || analysis.CandidateComponentId is null
            || string.IsNullOrWhiteSpace(analysis.ProcessType)
            || !string.Equals(
                analysis.Fingerprint,
                request.Fingerprint.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure("生产绑定证据有冲突或已变更，未执行写入。");
        }

        if (await bindingQueryService.GetByDeviceIdAsync(
                request.DeviceId,
                cancellationToken) is not null
            || await bindingQueryService.IsComponentBoundAsync(
                analysis.CandidateComponentId.Value,
                cancellationToken))
        {
            return Result.Failure("设备或插件发布系列已建立绑定。");
        }

        var appliedAtUtc = DateTime.UtcNow;
        var applyResult = await applyStore.TryApplyAsync(
            new ProductionBindingApplyRequest(
                Guid.NewGuid(),
                request.DeviceId,
                analysis.CandidateComponentId.Value,
                analysis.ProcessType,
                appliedAtUtc,
                Guid.NewGuid(),
                new AuditTrailEntry(
                    Guid.TryParse(currentUser.Id, out var actorId) ? actorId : null,
                    currentUser.UserName,
                    "DevicePluginBinding.ProductionApply",
                    "Device",
                    request.DeviceId.ToString("D"),
                    appliedAtUtc,
                    true,
                    $"根据四类权威证据显式应用生产设备—插件绑定 {analysis.ModuleId}@{analysis.PluginVersion}。",
                    IdempotencyKey: $"production-binding:{request.DeviceId:N}:{request.Fingerprint.ToLowerInvariant()}")),
            cancellationToken);
        if (applyResult is not (
                ProductionBindingApplyResult.Applied
                or ProductionBindingApplyResult.Idempotent))
        {
            return Result.Failure("生产绑定与审计未能原子写入，未自动猜测或覆盖。");
        }

        return await migrationService.AnalyzeAsync(
            request.DeviceId,
            cancellationToken);
    }
}
