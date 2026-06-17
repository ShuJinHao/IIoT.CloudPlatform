using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Production.Specifications.ClientReleases;

public sealed class ClientHostReleaseByIdentitySpec : Specification<ClientHostRelease>
{
    public ClientHostReleaseByIdentitySpec(string channel, string version, string targetRuntime)
    {
        var normalizedChannel = channel.Trim();
        var normalizedVersion = version.Trim();
        var normalizedTargetRuntime = targetRuntime.Trim();

        FilterCondition = release =>
            release.Channel == normalizedChannel
            && release.Version == normalizedVersion
            && release.TargetRuntime == normalizedTargetRuntime;
    }
}

public sealed class ClientHostReleaseByIdSpec : Specification<ClientHostRelease>
{
    public ClientHostReleaseByIdSpec(Guid releaseId)
    {
        FilterCondition = release => release.Id == releaseId;
    }
}

public sealed class ClientHostReleasesByChannelSpec : Specification<ClientHostRelease>
{
    public ClientHostReleasesByChannelSpec(
        string? channel,
        string? targetRuntime,
        bool onlyPublished,
        bool includeArchived = false)
    {
        var normalizedChannel = channel?.Trim();
        var normalizedTargetRuntime = targetRuntime?.Trim();

        FilterCondition = release =>
            (string.IsNullOrEmpty(normalizedChannel) || release.Channel == normalizedChannel)
            && (string.IsNullOrEmpty(normalizedTargetRuntime) || release.TargetRuntime == normalizedTargetRuntime)
            && (!onlyPublished
                || release.Status == ClientReleaseStatus.Published
                || release.Status == ClientReleaseStatus.Deprecated)
            && (includeArchived || release.Status != ClientReleaseStatus.Archived);

        SetOrderByDescending(release => release.PublishedAtUtc ?? release.CreatedAtUtc);
    }
}

public sealed class ClientHostReleasesForRetentionSpec : Specification<ClientHostRelease>
{
    public ClientHostReleasesForRetentionSpec(string channel, string targetRuntime)
    {
        var normalizedChannel = channel.Trim();
        var normalizedTargetRuntime = targetRuntime.Trim();

        FilterCondition = release =>
            release.Channel == normalizedChannel
            && release.TargetRuntime == normalizedTargetRuntime
            && release.Status == ClientReleaseStatus.Published;

        SetOrderByDescending(release => release.PublishedAtUtc ?? release.CreatedAtUtc);
    }
}
