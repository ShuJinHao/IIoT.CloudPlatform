using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Production.Specifications.ClientReleases;

public sealed class ClientReleaseComponentByIdentitySpec : Specification<ClientReleaseComponent>
{
    public ClientReleaseComponentByIdentitySpec(
        ClientReleaseComponentKind componentKind,
        string componentKey,
        string channel,
        string targetRuntime)
    {
        var normalizedComponentKey = componentKey.Trim();
        var normalizedChannel = channel.Trim();
        var normalizedTargetRuntime = targetRuntime.Trim();

        FilterCondition = component =>
            component.ComponentKind == componentKind
            && component.ComponentKey == normalizedComponentKey
            && component.Channel == normalizedChannel
            && component.TargetRuntime == normalizedTargetRuntime;

        AddInclude(component => component.Versions);
        AddInclude("Versions.Artifacts");
    }
}

public sealed class ClientReleaseComponentByVersionIdSpec : Specification<ClientReleaseComponent>
{
    public ClientReleaseComponentByVersionIdSpec(Guid versionId)
    {
        FilterCondition = component => component.Versions.Any(version => version.Id == versionId);
        AddInclude(component => component.Versions);
        AddInclude("Versions.Artifacts");
    }
}

public sealed class ClientReleaseComponentsByChannelSpec : Specification<ClientReleaseComponent>
{
    public ClientReleaseComponentsByChannelSpec(
        string? channel,
        string? targetRuntime,
        bool onlyPublished,
        bool includeArchived = false)
    {
        var normalizedChannel = channel?.Trim();
        var normalizedTargetRuntime = targetRuntime?.Trim();

        FilterCondition = component =>
            (string.IsNullOrEmpty(normalizedChannel) || component.Channel == normalizedChannel)
            && (string.IsNullOrEmpty(normalizedTargetRuntime) || component.TargetRuntime == normalizedTargetRuntime)
            && component.Versions.Any(version =>
                (!onlyPublished
                    || version.Status == ClientReleaseStatus.Published
                    || version.Status == ClientReleaseStatus.Deprecated)
                && (includeArchived
                    || (version.Status != ClientReleaseStatus.Archived
                        && version.Status != ClientReleaseStatus.Deleted
                        && version.Status != ClientReleaseStatus.DeleteFailed)));

        AddInclude(component => component.Versions);
        AddInclude("Versions.Artifacts");
        SetOrderByDescending(component => component.UpdatedAtUtc);
    }
}

public sealed class ClientReleaseComponentsForRetentionSpec : Specification<ClientReleaseComponent>
{
    public ClientReleaseComponentsForRetentionSpec(
        ClientReleaseComponentKind componentKind,
        string componentKey,
        string channel,
        string targetRuntime)
    {
        var normalizedComponentKey = componentKey.Trim();
        var normalizedChannel = channel.Trim();
        var normalizedTargetRuntime = targetRuntime.Trim();

        FilterCondition = component =>
            component.ComponentKind == componentKind
            && component.ComponentKey == normalizedComponentKey
            && component.Channel == normalizedChannel
            && component.TargetRuntime == normalizedTargetRuntime
            && component.Versions.Any(version => version.Status == ClientReleaseStatus.Published);

        AddInclude(component => component.Versions);
        AddInclude("Versions.Artifacts");
        SetOrderByDescending(component => component.UpdatedAtUtc);
    }
}
