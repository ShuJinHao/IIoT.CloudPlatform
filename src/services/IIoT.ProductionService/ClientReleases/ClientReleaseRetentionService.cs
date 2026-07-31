using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Persistence;
using IIoT.Services.CrossCutting.Persistence;
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
    IOptions<EdgeReleaseRetentionOptions> options,
    IUnitOfWork unitOfWork,
    IClientReleaseWriteObservationReader observationReader)
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
        await ApplyPolicyAsync(
                ClientReleaseComponentKind.Host,
                ClientReleaseComponent.HostComponentKey,
                channel,
                targetRuntime,
                cancellationToken);
    }

    public async Task ApplyPluginPolicyAsync(
        string moduleId,
        string channel,
        string targetRuntime,
        CancellationToken cancellationToken = default)
    {
        await ApplyPolicyAsync(
            ClientReleaseComponentKind.Plugin,
            moduleId,
            channel,
            targetRuntime,
            cancellationToken);
    }

    private async Task ApplyPolicyAsync(
        ClientReleaseComponentKind componentKind,
        string componentKey,
        string channel,
        string targetRuntime,
        CancellationToken cancellationToken)
    {
        var changedAtUtc =
            ClientReleaseWriteCommitRecovery.NormalizeUtc(
                DateTime.UtcNow);
        RetentionWritePlan? lastWritePlan = null;
        try
        {
            await unitOfWork.ExecuteResilientAsync(
                ExecuteAttemptAsync,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            if (lastWritePlan is not null)
            {
                _ = await CloudWriteCommitRecovery.TryObserveCommitAsync(
                    token => observationReader.ObserveVersionsAsync(
                        lastWritePlan.VersionIds,
                        token));
            }

            throw new OperationCanceledException(cancellationToken);
        }
        catch (CloudWriteException)
        {
            throw;
        }
        catch
        {
            var plan = lastWritePlan
                       ?? throw new CloudWriteCommitUnknownException();
            var current =
                await CloudWriteCommitRecovery.TryObserveCommitAsync(
                    token => observationReader.ObserveVersionsAsync(
                        plan.VersionIds,
                        token));
            if (current is null || MatchesBaseline(plan, current))
            {
                throw new CloudWriteCommitUnknownException();
            }

            if (MatchesTarget(plan, current))
            {
                return;
            }

            throw new CloudWriteConflictException();
        }

        async Task<bool> ExecuteAttemptAsync(
            CancellationToken callbackCancellationToken)
        {
            if (lastWritePlan is not null)
            {
                var prior =
                    await CloudWriteCommitRecovery.TryObserveAttemptAsync(
                        token => observationReader.ObserveVersionsAsync(
                            lastWritePlan.VersionIds,
                            token),
                        callbackCancellationToken)
                    ?? throw new CloudWriteCommitUnknownException();
                if (MatchesTarget(lastWritePlan, prior))
                {
                    return true;
                }

                if (!MatchesBaseline(lastWritePlan, prior))
                {
                    throw new CloudWriteConflictException();
                }
            }

            var maxVersions =
                await GetMaxVersionsPerComponentAsync(
                    callbackCancellationToken);
            var attemptComponent =
                await componentRepository.GetSingleOrDefaultAsync(
                    new ClientReleaseComponentsForRetentionSpec(
                        componentKind,
                        componentKey,
                        channel,
                        targetRuntime),
                    callbackCancellationToken);
            if (attemptComponent is null)
            {
                lastWritePlan = null;
                return true;
            }

            var ordered = attemptComponent.Versions
                .Where(release =>
                    release.Status == ClientReleaseStatus.Published)
                .OrderByDescending(
                    release => release.Version,
                    VersionComparer)
                .ThenByDescending(
                    release =>
                        release.PublishedAtUtc
                        ?? release.CreatedAtUtc)
                .ToList();
            if (ordered.Count <= maxVersions)
            {
                lastWritePlan = null;
                return true;
            }

            var snapshots =
                await clientStateStore
                    .GetVersionSnapshotsByDevicesAsync(
                        cancellationToken:
                        callbackCancellationToken);
            var targets = ordered
                .Skip(maxVersions)
                .ToDictionary(
                    release => release.Id,
                    release => componentKind
                               == ClientReleaseComponentKind.Host
                        ? IsHostInUse(
                            attemptComponent,
                            release,
                            snapshots)
                            ? ClientReleaseStatus.Deprecated
                            : ClientReleaseStatus.Archived
                        : IsPluginInUse(
                            attemptComponent,
                            release,
                            snapshots)
                            ? ClientReleaseStatus.Deprecated
                            : ClientReleaseStatus.Archived);
            if (targets.Count == 0)
            {
                lastWritePlan = null;
                return true;
            }

            var baseline =
                await CloudWriteCommitRecovery.TryObserveAttemptAsync(
                    token => observationReader.ObserveVersionsAsync(
                        targets.Keys.ToArray(),
                        token),
                    callbackCancellationToken)
                ?? throw new CloudWriteCommitUnknownException();
            if (baseline.Count != targets.Count)
            {
                throw new CloudWriteConflictException();
            }

            var baselineById = baseline.ToDictionary(
                state => state.VersionId);
            foreach (var (versionId, targetStatus) in targets)
            {
                var version = attemptComponent.FindVersion(versionId)
                              ?? throw new CloudWriteConflictException();
                if (ClientReleaseWriteStateFingerprint.ForVersion(
                        attemptComponent,
                        version)
                    != baselineById[versionId])
                {
                    throw new CloudWriteConflictException();
                }

                attemptComponent.ChangeVersionStatus(
                    versionId,
                    targetStatus,
                    changedAtUtc);
            }

            lastWritePlan = new RetentionWritePlan(
                baselineById,
                targets);
            await componentRepository.SaveChangesAsync(
                callbackCancellationToken);
            return true;
        }
    }

    private static bool MatchesBaseline(
        RetentionWritePlan plan,
        IReadOnlyCollection<ClientReleaseVersionWriteState> current)
        => current.Count == plan.BaselineById.Count
           && current.All(state =>
               plan.BaselineById.TryGetValue(
                   state.VersionId,
                   out var expected)
               && state == expected);

    private static bool MatchesTarget(
        RetentionWritePlan plan,
        IReadOnlyCollection<ClientReleaseVersionWriteState> current)
    {
        if (current.Count != plan.Targets.Count)
        {
            return false;
        }

        foreach (var state in current)
        {
            if (!plan.BaselineById.TryGetValue(
                    state.VersionId,
                    out var original)
                || !plan.Targets.TryGetValue(
                    state.VersionId,
                    out var targetStatus)
                || !ClientReleaseWriteCommitRecovery
                    .MatchesVersionTarget(
                        state,
                        original,
                        targetStatus))
            {
                return false;
            }
        }

        return true;
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

    private sealed record RetentionWritePlan(
        IReadOnlyDictionary<Guid, ClientReleaseVersionWriteState>
            BaselineById,
        IReadOnlyDictionary<Guid, ClientReleaseStatus> Targets)
    {
        public Guid[] VersionIds { get; } =
            Targets.Keys
                .OrderBy(id => id)
                .ToArray();
    }
}
