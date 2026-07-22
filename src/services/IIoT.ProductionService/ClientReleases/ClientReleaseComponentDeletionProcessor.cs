using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.SharedKernel.Repository;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.ClientReleases;

public interface IClientReleaseComponentDeletionProcessor
{
    Task<ClientReleaseComponentDeletionOutcome> ProcessAsync(
        ClientReleaseComponentDeletion deletion,
        CancellationToken cancellationToken);
}

/// <summary>
/// 执行一个持久化删除操作的文件清理，并在收敛后删除操作记录。
/// 成功只留审计；失败把操作标为 Failed，等待管理员显式重试或启动恢复。
/// </summary>
public sealed class ClientReleaseComponentDeletionProcessor(
    IOptions<EdgeInstallerArtifactOptions> artifactOptions,
    IRepository<ClientReleaseComponent> componentRepository,
    IClientReleaseComponentDeletionStore deletionStore,
    ILogger<ClientReleaseComponentDeletionProcessor> logger)
    : IClientReleaseComponentDeletionProcessor
{
    public async Task<ClientReleaseComponentDeletionOutcome> ProcessAsync(
        ClientReleaseComponentDeletion deletion,
        CancellationToken cancellationToken)
    {
        var survivingNupkgFileNames = await CollectSurvivingNupkgFileNamesAsync(
            deletion,
            cancellationToken);
        var outcome = new ClientReleaseComponentDeletionExecutor(artifactOptions, logger)
            .Execute(deletion, survivingNupkgFileNames, cancellationToken);
        if (outcome.Succeeded)
        {
            deletionStore.Remove(deletion);
            await deletionStore.SaveChangesAsync(cancellationToken);
            return outcome;
        }

        deletion.MarkFailed(outcome.FailureCode!);
        await deletionStore.SaveChangesAsync(cancellationToken);
        return outcome;
    }

    private async Task<IReadOnlyCollection<string>> CollectSurvivingNupkgFileNamesAsync(
        ClientReleaseComponentDeletion deletion,
        CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.Equals(deletion.ComponentKind, "Host", StringComparison.OrdinalIgnoreCase))
        {
            return names;
        }

        // 白名单覆盖同 channel 全部 runtime 的存活组件（不区分 targetRuntime），
        // 任何存活 Host/Plugin 版本仍引用的 .nupkg 都是共享包，不得删除。
        var survivingComponents = await componentRepository.GetListAsync(
            new ClientReleaseComponentsByChannelSpec(
                deletion.Channel,
                targetRuntime: null,
                onlyPublished: false),
            cancellationToken);
        foreach (var component in survivingComponents)
        {
            foreach (var version in component.Versions)
            {
                foreach (var artifact in version.Artifacts)
                {
                    if (artifact.ArtifactKind == ClientReleaseArtifactKind.VelopackFile
                        && artifact.RelativePath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                    {
                        names.Add(Path.GetFileName(artifact.RelativePath));
                    }
                }
            }
        }

        return names;
    }
}
