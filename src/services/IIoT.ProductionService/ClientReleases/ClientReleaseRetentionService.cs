using IIoT.Core.Production.Aggregates.ClientReleases;
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
    IRepository<ClientHostRelease> hostRepository,
    IRepository<ClientPluginRelease> pluginRepository,
    IReadRepository<DeviceClientVersionSnapshot> snapshotRepository,
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
        var releases = await hostRepository.GetListAsync(
            new ClientHostReleasesForRetentionSpec(channel, targetRuntime),
            cancellationToken);

        var ordered = releases
            .OrderByDescending(release => release.Version, VersionComparer)
            .ThenByDescending(release => release.PublishedAtUtc ?? release.CreatedAtUtc)
            .ToList();

        if (ordered.Count <= maxVersions)
        {
            return;
        }

        var snapshots = await snapshotRepository.GetListAsync(
            new DeviceClientVersionSnapshotsByDevicesSpec(),
            cancellationToken);

        foreach (var release in ordered.Skip(maxVersions))
        {
            release.ChangeStatus(IsHostInUse(release, snapshots)
                ? ClientReleaseStatus.Deprecated
                : ClientReleaseStatus.Archived);
        }

        await hostRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task ApplyPluginPolicyAsync(
        string moduleId,
        string channel,
        string targetRuntime,
        CancellationToken cancellationToken = default)
    {
        var maxVersions = await GetMaxVersionsPerComponentAsync(cancellationToken);
        var releases = await pluginRepository.GetListAsync(
            new ClientPluginReleasesForRetentionSpec(moduleId, channel, targetRuntime),
            cancellationToken);

        var ordered = releases
            .OrderByDescending(release => release.Version, VersionComparer)
            .ThenByDescending(release => release.PublishedAtUtc ?? release.CreatedAtUtc)
            .ToList();

        if (ordered.Count <= maxVersions)
        {
            return;
        }

        var snapshots = await snapshotRepository.GetListAsync(
            new DeviceClientVersionSnapshotsByDevicesSpec(),
            cancellationToken);

        foreach (var release in ordered.Skip(maxVersions))
        {
            release.ChangeStatus(IsPluginInUse(release, snapshots)
                ? ClientReleaseStatus.Deprecated
                : ClientReleaseStatus.Archived);
        }

        await pluginRepository.SaveChangesAsync(cancellationToken);
    }

    private static bool IsHostInUse(
        ClientHostRelease release,
        IEnumerable<DeviceClientVersionSnapshot> snapshots)
    {
        return snapshots.Any(snapshot =>
            string.Equals(snapshot.Channel, release.Channel, StringComparison.OrdinalIgnoreCase)
            && string.Equals(snapshot.HostVersion, release.Version, StringComparison.OrdinalIgnoreCase)
            && string.Equals(snapshot.HostApiVersion, release.HostApiVersion, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPluginInUse(
        ClientPluginRelease release,
        IEnumerable<DeviceClientVersionSnapshot> snapshots)
    {
        return snapshots.Any(snapshot =>
            string.Equals(snapshot.Channel, release.Channel, StringComparison.OrdinalIgnoreCase)
            && snapshot.InstalledPlugins.Any(plugin =>
                string.Equals(plugin.ModuleId, release.ModuleId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(plugin.Version, release.Version, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(plugin.HostApiVersion)
                    || string.Equals(plugin.HostApiVersion, release.HostApiVersion, StringComparison.OrdinalIgnoreCase))));
    }
}
