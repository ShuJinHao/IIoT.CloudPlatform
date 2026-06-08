using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Production.Specifications.ClientReleases;

public sealed class ClientPluginReleaseByIdentitySpec : Specification<ClientPluginRelease>
{
    public ClientPluginReleaseByIdentitySpec(
        string moduleId,
        string channel,
        string version,
        string targetRuntime)
    {
        var normalizedModuleId = moduleId.Trim();
        var normalizedChannel = channel.Trim();
        var normalizedVersion = version.Trim();
        var normalizedTargetRuntime = targetRuntime.Trim();

        FilterCondition = release =>
            release.ModuleId == normalizedModuleId
            && release.Channel == normalizedChannel
            && release.Version == normalizedVersion
            && release.TargetRuntime == normalizedTargetRuntime;
    }
}

public sealed class ClientPluginReleasesByChannelSpec : Specification<ClientPluginRelease>
{
    public ClientPluginReleasesByChannelSpec(string? channel, string? targetRuntime, bool onlyPublished)
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
