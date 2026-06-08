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

public sealed class ClientHostReleasesByChannelSpec : Specification<ClientHostRelease>
{
    public ClientHostReleasesByChannelSpec(string? channel, string? targetRuntime, bool onlyPublished)
    {
        var normalizedChannel = channel?.Trim();
        var normalizedTargetRuntime = targetRuntime?.Trim();

        FilterCondition = release =>
            (string.IsNullOrEmpty(normalizedChannel) || release.Channel == normalizedChannel)
            && (string.IsNullOrEmpty(normalizedTargetRuntime) || release.TargetRuntime == normalizedTargetRuntime)
            && (!onlyPublished || release.Status == ClientReleaseStatus.Published);

        SetOrderByDescending(release => release.PublishedAtUtc ?? release.CreatedAtUtc);
    }
}
