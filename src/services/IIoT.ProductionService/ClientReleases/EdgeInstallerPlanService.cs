using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.ProductionService.Commands.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.ClientReleases;

public sealed record EdgeInstallerPlanPluginDto(
    Guid DeviceId,
    string ClientCode,
    string DeviceName,
    Guid ProcessId,
    string ProcessType,
    Guid BindingId,
    Guid ComponentId,
    string ModuleId,
    string PluginVersion,
    string PluginSha256,
    string HostApiVersion,
    string? TargetFramework,
    string? FileManifestSha256 = null,
    string? DependencyClosureSha256 = null,
    string? DependencyHostVersion = null,
    string? DependencyHostFileManifestSha256 = null);

public sealed record EdgeInstallerPlanDto(
    string PlanFingerprint,
    string Channel,
    string TargetRuntime,
    string HostVersion,
    string HostSha256,
    string HostApiVersion,
    string? TargetFramework,
    IReadOnlyList<EdgeInstallerPlanPluginDto> Devices,
    string? HostFileManifestSha256 = null,
    IReadOnlyList<KeyValuePair<string, string>>? BindingRoutes = null);

public interface IEdgeInstallerPlanService
{
    Task<Result<EdgeInstallerPlanDto>> BuildAsync(
        IReadOnlyList<Guid> deviceIds,
        CancellationToken cancellationToken = default);
}

public sealed class EdgeInstallerPlanService(
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IReadRepository<Device> deviceRepository,
    IReadRepository<ClientReleaseComponent> componentRepository,
    IDevicePluginBindingQueryService bindingQueryService)
    : IEdgeInstallerPlanService
{
    private const string Channel = "stable";
    private const string TargetRuntime = "win-x64";
    private static readonly IComparer<string> VersionComparer =
        Comparer<string>.Create(ClientReleaseMapping.CompareVersions);
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<Result<EdgeInstallerPlanDto>> BuildAsync(
        IReadOnlyList<Guid> deviceIds,
        CancellationToken cancellationToken = default)
    {
        if (deviceIds is null
            || deviceIds.Count == 0
            || deviceIds.Any(id => id == Guid.Empty)
            || deviceIds.Distinct().Count() != deviceIds.Count)
        {
            return Result.Invalid(
                "安装计划必须选择至少一个且不重复的设备插件。");
        }

        var orderedIds = deviceIds.OrderBy(id => id).ToArray();
        var access = await currentUserDeviceAccessService
            .GetAccessibleDeviceIdsAsync(cancellationToken);
        if (!access.IsSuccess)
            return Result.Forbidden("用户设备权限无法确认。");
        if (access.Value is { } allowed
            && orderedIds.Any(id => !allowed.Contains(id)))
        {
            return Result.Forbidden("安装计划包含未授权设备。");
        }

        var devices = await deviceRepository.GetListAsync(
            new DevicePagedSpec(
                0,
                0,
                orderedIds.ToList(),
                isPaging: false),
            cancellationToken);
        if (devices.Count != orderedIds.Length)
            return Result.NotFound("安装计划中存在不存在或已删除的设备。");

        var deviceById = devices.ToDictionary(device => device.Id);
        var bindings = await bindingQueryService.GetByDeviceIdsAsync(
            orderedIds,
            cancellationToken);
        if (bindings.Count != orderedIds.Length)
            return Result.Invalid("所有设备必须先建立权威一对一插件绑定。");

        var bindingByDevice = bindings.ToDictionary(binding => binding.DeviceId);
        var componentById = new Dictionary<Guid, ClientReleaseComponent>();
        foreach (var binding in bindings)
        {
            var device = deviceById[binding.DeviceId];
            var component = await componentRepository.GetSingleOrDefaultAsync(
                new ClientReleaseComponentByComponentIdSpec(
                    binding.ComponentId),
                cancellationToken);
            if (component is null
                || component.ComponentKind != ClientReleaseComponentKind.Plugin
                || !string.Equals(component.Channel, Channel, StringComparison.Ordinal)
                || !string.Equals(
                    component.TargetRuntime,
                    TargetRuntime,
                    StringComparison.Ordinal)
                || !string.Equals(
                    component.SupportedProcessType,
                    binding.SupportedProcessType,
                    StringComparison.Ordinal))
            {
                return Result.Invalid(
                    $"设备 [{device.DeviceName}] 的绑定插件系列不可用。");
            }

            componentById.Add(component.Id, component);
        }

        var host = await componentRepository.GetSingleOrDefaultAsync(
            new ClientReleaseComponentByIdentitySpec(
                ClientReleaseComponentKind.Host,
                ClientReleaseComponent.HostComponentKey,
                Channel,
                TargetRuntime),
            cancellationToken);
        if (host is null)
            return Result.NotFound("没有已发布的 stable/win-x64 Host。");

        foreach (var hostVersion in host.Versions
                     .Where(version =>
                         version.Status == ClientReleaseStatus.Published)
                     .OrderByDescending(
                         version => version.Version,
                         VersionComparer)
                     .ThenByDescending(version =>
                         version.PublishedAtUtc ?? version.CreatedAtUtc))
        {
            var selected = new List<EdgeInstallerPlanPluginDto>(orderedIds.Length);
            foreach (var deviceId in orderedIds)
            {
                var device = deviceById[deviceId];
                var binding = bindingByDevice[deviceId];
                var component = componentById[binding.ComponentId];
                var pluginVersion = component.Versions
                    .Where(version =>
                        version.Status == ClientReleaseStatus.Published
                        && ClientReleaseMapping.IsCompatibleWithHost(
                            version,
                            hostVersion.Version,
                            hostVersion.HostApiVersion,
                            out _)
                        && HasExactHostEvidence(
                            component,
                            version,
                            hostVersion))
                    .OrderByDescending(
                        version => version.Version,
                        VersionComparer)
                    .ThenByDescending(version =>
                        version.PublishedAtUtc ?? version.CreatedAtUtc)
                    .FirstOrDefault();
                if (pluginVersion is null)
                {
                    selected.Clear();
                    break;
                }

                selected.Add(new EdgeInstallerPlanPluginDto(
                    device.Id,
                    device.Code,
                    device.DeviceName,
                    device.ProcessId,
                    binding.SupportedProcessType,
                    binding.BindingId,
                    binding.ComponentId,
                    binding.ModuleId,
                    pluginVersion.Version,
                    pluginVersion.Sha256,
                    pluginVersion.HostApiVersion,
                    pluginVersion.TargetFramework,
                    pluginVersion.FileManifestSha256,
                    pluginVersion.DependencyClosureSha256,
                    pluginVersion.DependencyHostVersion,
                    pluginVersion.DependencyHostFileManifestSha256));
            }

            if (selected.Count != orderedIds.Length)
                continue;

            var fingerprint = ComputeFingerprint(
                hostVersion,
                selected);
            return Result.Success(new EdgeInstallerPlanDto(
                fingerprint,
                Channel,
                TargetRuntime,
                hostVersion.Version,
                hostVersion.Sha256,
                hostVersion.HostApiVersion,
                hostVersion.TargetFramework,
                selected,
                hostVersion.FileManifestSha256,
                EdgeBindingWireSchema.OrderedRoutes));
        }

        return Result.Invalid(
            "所选设备插件没有共同兼容的已批准 stable/win-x64 Host。");
    }

    private static string ComputeFingerprint(
        ClientReleaseVersion host,
        IReadOnlyList<EdgeInstallerPlanPluginDto> devices)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            channel = Channel,
            targetRuntime = TargetRuntime,
            host = new
            {
                host.Version,
                host.Sha256,
                host.HostApiVersion,
                host.TargetFramework,
                host.FileManifestSha256
            },
            bindingSchemaVersion = EdgeBindingWireSchema.SchemaVersion,
            bindingRoutes = EdgeBindingWireSchema.OrderedRoutes.Select(route => new
            {
                route.Key,
                route.Value
            }),
            devices = devices
                .OrderBy(device => device.DeviceId)
                .Select(device => new
                {
                    device.DeviceId,
                    device.ClientCode,
                    device.ProcessId,
                    device.ProcessType,
                    device.BindingId,
                    device.ComponentId,
                    device.ModuleId,
                    device.PluginVersion,
                    device.PluginSha256,
                    device.HostApiVersion,
                    device.TargetFramework,
                    device.FileManifestSha256,
                    device.DependencyClosureSha256,
                    device.DependencyHostVersion,
                    device.DependencyHostFileManifestSha256
                })
        }, JsonOptions);
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static bool HasExactHostEvidence(
        ClientReleaseComponent component,
        ClientReleaseVersion pluginVersion,
        ClientReleaseVersion hostVersion)
    {
        var hasAnyEvidence = pluginVersion.DependencyClosureSha256 is not null
                             || pluginVersion.DependencyHostVersion is not null
                             || pluginVersion.DependencyHostFileManifestSha256 is not null;
        if (!hasAnyEvidence)
        {
            return component.ManifestSchemaVersion < 3;
        }

        return ClientReleaseFileFacts.IsSha256(
                   pluginVersion.DependencyClosureSha256)
               && ClientReleaseFileFacts.IsSha256(
                   pluginVersion.DependencyHostFileManifestSha256)
               && ClientReleaseFileFacts.IsSha256(hostVersion.FileManifestSha256)
               && string.Equals(
                   pluginVersion.DependencyHostVersion,
                   hostVersion.Version,
                   StringComparison.Ordinal)
               && string.Equals(
                   pluginVersion.DependencyHostFileManifestSha256,
                   hostVersion.FileManifestSha256,
                   StringComparison.OrdinalIgnoreCase);
    }
}

[AuthorizeRequirement(ClientReleasePermissions.GenerateInstaller)]
public sealed record GetEdgeInstallerPlanQuery(
    IReadOnlyList<Guid> DeviceIds)
    : IHumanQuery<Result<EdgeInstallerPlanDto>>;

public sealed class GetEdgeInstallerPlanHandler(
    IEdgeInstallerPlanService planService)
    : IQueryHandler<GetEdgeInstallerPlanQuery, Result<EdgeInstallerPlanDto>>
{
    public Task<Result<EdgeInstallerPlanDto>> Handle(
        GetEdgeInstallerPlanQuery request,
        CancellationToken cancellationToken)
        => planService.BuildAsync(request.DeviceIds, cancellationToken);
}
