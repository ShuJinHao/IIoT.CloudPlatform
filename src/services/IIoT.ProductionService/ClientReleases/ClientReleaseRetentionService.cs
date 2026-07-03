using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.SharedKernel.Repository;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.ClientReleases;

public interface IClientReleaseRetentionPolicyReader
{
    Task<int> GetMaxVersionsPerComponentAsync(CancellationToken cancellationToken = default);
}

public interface IClientReleaseRetentionService : IClientReleaseRetentionPolicyReader
{
    Task ApplyHostPolicyAsync(string channel, string targetRuntime, CancellationToken cancellationToken = default);

    Task ApplyPluginPolicyAsync(
        string moduleId,
        string channel,
        string targetRuntime,
        CancellationToken cancellationToken = default);
}

public sealed class ClientReleaseRetentionService(
    IRepository<ClientReleaseRetentionPolicy> policyRepository,
    IRepository<ClientReleaseComponent> componentRepository,
    IDeviceClientStateStore clientStateStore,
    IOptions<EdgeReleaseRetentionOptions> options)
    : IClientReleaseRetentionService
{
    private static readonly IComparer<string> VersionComparer = Comparer<string>.Create(ClientReleaseMapping.CompareVersions);

    public async Task<int> GetMaxVersionsPerComponentAsync(CancellationToken cancellationToken = default)
    {
        var policy = await policyRepository.GetSingleOrDefaultAsync(
            new ClientReleaseRetentionPolicyByIdSpec(),
            cancellationToken);

        return policy?.MaxVersionsPerComponent ?? options.Value.MaxVersionsPerComponent;
    }

    public async Task ApplyHostPolicyAsync(
        string channel,
        string targetRuntime,
        CancellationToken cancellationToken = default)
    {
        var maxVersions = await GetMaxVersionsPerComponentAsync(cancellationToken);
        var component = await componentRepository.GetSingleOrDefaultAsync(
            new ClientReleaseComponentsForRetentionSpec(
                ClientReleaseComponentKind.Host,
                ClientReleaseComponent.HostComponentKey,
                channel,
                targetRuntime),
            cancellationToken);
        if (component is null)
        {
            return;
        }

        var ordered = component.Versions
            .Where(release => release.Status == ClientReleaseStatus.Published)
            .OrderByDescending(release => release.Version, VersionComparer)
            .ThenByDescending(release => release.PublishedAtUtc ?? release.CreatedAtUtc)
            .ToList();

        if (ordered.Count <= maxVersions)
        {
            return;
        }

        var snapshots = await clientStateStore.GetVersionSnapshotsByDevicesAsync(cancellationToken: cancellationToken);

        foreach (var release in ordered.Skip(maxVersions))
        {
            release.ChangeStatus(IsHostInUse(component, release, snapshots)
                ? ClientReleaseStatus.Deprecated
                : ClientReleaseStatus.Archived);
        }

        await componentRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task ApplyPluginPolicyAsync(
        string moduleId,
        string channel,
        string targetRuntime,
        CancellationToken cancellationToken = default)
    {
        var maxVersions = await GetMaxVersionsPerComponentAsync(cancellationToken);
        var component = await componentRepository.GetSingleOrDefaultAsync(
            new ClientReleaseComponentsForRetentionSpec(
                ClientReleaseComponentKind.Plugin,
                moduleId,
                channel,
                targetRuntime),
            cancellationToken);
        if (component is null)
        {
            return;
        }

        var ordered = component.Versions
            .Where(release => release.Status == ClientReleaseStatus.Published)
            .OrderByDescending(release => release.Version, VersionComparer)
            .ThenByDescending(release => release.PublishedAtUtc ?? release.CreatedAtUtc)
            .ToList();

        if (ordered.Count <= maxVersions)
        {
            return;
        }

        var snapshots = await clientStateStore.GetVersionSnapshotsByDevicesAsync(cancellationToken: cancellationToken);

        foreach (var release in ordered.Skip(maxVersions))
        {
            release.ChangeStatus(IsPluginInUse(component, release, snapshots)
                ? ClientReleaseStatus.Deprecated
                : ClientReleaseStatus.Archived);
        }

        await componentRepository.SaveChangesAsync(cancellationToken);
    }

    private static bool IsHostInUse(
        ClientReleaseComponent component,
        ClientReleaseVersion release,
        IEnumerable<DeviceClientVersionSnapshot> snapshots)
    {
        return snapshots.Any(snapshot =>
            string.Equals(snapshot.Channel, component.Channel, StringComparison.OrdinalIgnoreCase)
            && string.Equals(snapshot.HostVersion, release.Version, StringComparison.OrdinalIgnoreCase)
            && string.Equals(snapshot.HostApiVersion, release.HostApiVersion, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPluginInUse(
        ClientReleaseComponent component,
        ClientReleaseVersion release,
        IEnumerable<DeviceClientVersionSnapshot> snapshots)
    {
        return snapshots.Any(snapshot =>
            string.Equals(snapshot.Channel, component.Channel, StringComparison.OrdinalIgnoreCase)
            && snapshot.InstalledPlugins.Any(plugin =>
                string.Equals(plugin.ModuleId, component.ComponentKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(plugin.Version, release.Version, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(plugin.HostApiVersion)
                    || string.Equals(plugin.HostApiVersion, release.HostApiVersion, StringComparison.OrdinalIgnoreCase))));
    }
}
