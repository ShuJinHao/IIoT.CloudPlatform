using System.Text.Json;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Domain;
using IIoT.SharedKernel.Result;
using IIoT.SharedKernel.Architecture;

namespace IIoT.ProductionService.ClientReleases;

public sealed record ResolvedDevicePluginDataCapability(
    string ClientCode,
    string ProcessType,
    string PluginVersion,
    PluginDataCapability Capability);

public interface IDevicePluginDataCapabilityResolver : IReadOnlyQueryPort
{
    Task<Result<ResolvedDevicePluginDataCapability>> ResolveAsync(
        Guid deviceId,
        string typeKey,
        CancellationToken cancellationToken = default);
}

public sealed class DevicePluginDataCapabilityResolver(
    IDeviceIdentityQueryService deviceIdentityQuery,
    IDevicePluginBindingQueryService bindingQueryService,
    IDeviceClientStateQueryService clientStateQueryService,
    IReadRepository<ClientReleaseComponent> componentRepository)
    : IDevicePluginDataCapabilityResolver
{
    public async Task<Result<ResolvedDevicePluginDataCapability>> ResolveAsync(
        Guid deviceId,
        string typeKey,
        CancellationToken cancellationToken = default)
    {
        var device = await deviceIdentityQuery.GetByDeviceIdAsync(
            deviceId,
            cancellationToken);
        if (device is null)
            return Result.NotFound("设备不存在。");
        if (string.IsNullOrWhiteSpace(device.ProcessCode))
            return Result.Invalid("设备未登记有效工序。");

        var binding = await bindingQueryService.GetByDeviceIdAsync(
            deviceId,
            cancellationToken);
        if (binding is null)
        {
            return Result.Invalid(
                "capability_unavailable: 设备尚未建立权威插件绑定。");
        }

        var snapshot = await clientStateQueryService
            .GetVersionSnapshotByDeviceAsync(deviceId, cancellationToken);
        var installed = snapshot?.InstalledPlugins.SingleOrDefault(plugin =>
            plugin.Enabled
            && string.Equals(
                plugin.ModuleId,
                binding.ModuleId,
                StringComparison.OrdinalIgnoreCase));
        if (installed is null)
        {
            return Result.Invalid(
                "capability_unavailable: 设备尚未上报已绑定插件的实际安装版本。");
        }

        var component = await componentRepository.GetSingleOrDefaultAsync(
            new ClientReleaseComponentByComponentIdSpec(binding.ComponentId),
            cancellationToken);
        var version = component?.FindVersion(installed.Version);
        if (version is null
            || version.Status is ClientReleaseStatus.Deleted
                or ClientReleaseStatus.DeleteRequested
                or ClientReleaseStatus.DeleteFailed)
        {
            return Result.Invalid(
                "capability_unavailable: 实际安装版本没有可用的数据能力清单。");
        }

        var packageEvidenceError = DevicePluginPackageEvidence.Validate(
            component!,
            version,
            installed.PackageSha256);
        if (packageEvidenceError is not null)
        {
            return Result.Invalid(packageEvidenceError);
        }

        IReadOnlyList<PluginDataCapability> capabilities;
        try
        {
            capabilities = PluginDataCapabilityCatalog.Parse(
                version.DataCapabilitiesJson);
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidDataException)
        {
            return Result.Invalid(
                "capability_unavailable: 实际安装版本的数据能力清单无效。");
        }

        var capability = PluginDataCapabilityCatalog.Resolve(
            capabilities,
            typeKey);
        if (capability is null)
        {
            return Result.Forbidden(
                $"业务记录类别 [{typeKey}] 不属于该设备当前实际安装的插件版本。");
        }

        return Result.Success(new ResolvedDevicePluginDataCapability(
            device.Code,
            BusinessIdentityNormalization.NormalizeClassificationCode(
                device.ProcessCode,
                nameof(device.ProcessCode)),
            installed.Version,
            capability));
    }
}

internal static class DevicePluginPackageEvidence
{
    public static string? Validate(
        ClientReleaseComponent component,
        ClientReleaseVersion version,
        string? installedPackageSha256)
    {
        var normalized = installedPackageSha256?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return component.ManifestSchemaVersion >= 3
                ? "capability_unavailable: 实际安装插件未上报包摘要。"
                : null;
        }

        if (!ClientReleaseFileFacts.IsSha256(normalized)
            || !string.Equals(
                normalized,
                version.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return "capability_unavailable: 实际安装插件包摘要与发布记录不一致。";
        }

        return null;
    }
}
