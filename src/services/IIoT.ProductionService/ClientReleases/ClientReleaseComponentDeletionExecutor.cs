using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.ProductionService.Commands.ClientReleases;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.ClientReleases;

/// <summary>
/// 物理删除一个发布组件的全部受控文件。组件数据库元数据必须先于本执行器删除，
/// 本执行器只做纯文件操作，可以按同一输入幂等重放到全部目标收敛。
/// </summary>
internal sealed class ClientReleaseComponentDeletionExecutor(
    IOptions<EdgeInstallerArtifactOptions> artifactOptions,
    ILogger logger)
{
    public const string FailureFileDeletion = "FileDeletionFailed";
    public const string FailureManifestRebuild = "ManifestRebuildFailed";

    public ClientReleaseComponentDeletionOutcome Execute(
        ClientReleaseComponent component,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? sharedNupkgNames = null)
    {
        var edgeRoot = artifactOptions.Value.ResolveEdgeUpdatesRoot();
        ClientReleaseControlledDirectory.ValidateChain(
            edgeRoot,
            edgeRoot,
            "发布受控目录非法。");

        var relativePaths = ClientReleaseComponentRelativePaths.Collect(edgeRoot, component);
        var plan = ClientReleaseComponentDeletionPlan.ForComponent(edgeRoot, component);
        var deletedPaths = new List<string>();
        foreach (var target in plan.Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                target.AssertSafe();
                var hadFiles = target.RelativeFiles.Count > 0;
                target.Delete();
                if (hadFiles)
                {
                    deletedPaths.AddRange(target.RelativeFiles);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                ClientReleasePublishDiagnostics.LogFailure(
                    logger,
                    LogLevel.Warning,
                    new EventId(4610, "ClientReleaseComponentFileDeletionFailure"),
                    "component-file-deletion",
                    ex,
                    component.ComponentKey);
                return new ClientReleaseComponentDeletionOutcome(
                    false,
                    deletedPaths,
                    plan.SkippedPaths,
                    FailureFileDeletion);
            }
        }

        // 先重建 manifest 移除已不存在 .nupkg 的引用，再清掉不再被任何 manifest 引用的孤儿 .nupkg。
        var skippedPaths = plan.SkippedPaths.ToList();
        if (component.ComponentKind == ClientReleaseComponentKind.Host)
        {
            var velopackRoot = Path.Combine(edgeRoot, "velopack", component.Channel);
            var orphanNupkgs = relativePaths
                .Where(path =>
                    path.StartsWith($"velopack/{component.Channel}/", StringComparison.Ordinal)
                    && path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var removableNupkgs = orphanNupkgs
                .Where(name => sharedNupkgNames?.Contains(name) != true)
                .ToList();

            if (Directory.Exists(velopackRoot)
                && !ClientReleaseVelopackPaths.TryRebuildChannelManifests(
                    velopackRoot,
                    logger,
                    removableNupkgs))
            {
                return new ClientReleaseComponentDeletionOutcome(
                    false,
                    deletedPaths,
                    skippedPaths,
                    FailureManifestRebuild);
            }

            foreach (var name in removableNupkgs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.Combine(velopackRoot, name);
                if (!File.Exists(path))
                {
                    continue;
                }

                if (IsReferencedByChannelManifests(velopackRoot, name))
                {
                    skippedPaths.Add($"velopack/{component.Channel}/{name}");
                    continue;
                }

                try
                {
                    File.Delete(path);
                    deletedPaths.Add($"velopack/{component.Channel}/{name}");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    ClientReleasePublishDiagnostics.LogFailure(
                        logger,
                        LogLevel.Warning,
                        new EventId(4610, "ClientReleaseComponentFileDeletionFailure"),
                        "component-orphan-nupkg-deletion",
                        ex,
                        component.ComponentKey);
                    return new ClientReleaseComponentDeletionOutcome(
                        false,
                        deletedPaths,
                        skippedPaths,
                        FailureFileDeletion);
                }
            }
        }

        return new ClientReleaseComponentDeletionOutcome(
            true,
            deletedPaths,
            skippedPaths,
            null);
    }

    private static bool IsReferencedByChannelManifests(string velopackRoot, string fileName)
    {
        var manifestPaths = Directory.EnumerateFiles(velopackRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(candidate => ClientReleaseVelopackPaths.IsProtectedChannelManifest(
                Path.GetFileName(candidate)));
        return ClientReleaseVelopackPaths.IsReferencedByManifests(manifestPaths, fileName);
    }
}

internal sealed record ClientReleaseComponentDeletionOutcome(
    bool Succeeded,
    IReadOnlyList<string> DeletedPaths,
    IReadOnlyList<string> SkippedPaths,
    string? FailureCode);
