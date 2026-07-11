using System.Text.Json;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.Commands.ClientReleases;

[AuthorizeRequirement(ClientReleasePermissions.Manage)]
public sealed record DeleteClientReleasePackageCommand(Guid ReleaseId, string? Reason = null)
    : IHumanCommand<Result<ClientReleaseFileDeletionResultDto>>;

public sealed record ClientReleaseFileDeletionResultDto(
    Guid ReleaseId,
    string ComponentKind,
    string ComponentName,
    string Channel,
    string Version,
    bool FilesDeleted,
    IReadOnlyList<string> DeletedPaths,
    IReadOnlyList<string> SkippedPaths,
    string? Warning);

public sealed class DeleteClientReleasePackageHandler(
    IOptions<EdgeInstallerArtifactOptions> artifactOptions,
    IRepository<ClientReleaseComponent> componentRepository,
    IDeviceClientStateStore clientStateStore,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService)
    : ICommandHandler<DeleteClientReleasePackageCommand, Result<ClientReleaseFileDeletionResultDto>>
{
    public async Task<Result<ClientReleaseFileDeletionResultDto>> Handle(
        DeleteClientReleasePackageCommand request,
        CancellationToken cancellationToken)
    {
        var component = await componentRepository.GetSingleOrDefaultAsync(
            new ClientReleaseComponentByVersionIdSpec(request.ReleaseId),
            cancellationToken);
        if (component is null)
        {
            return Result.NotFound("发布版本不存在。");
        }

        var version = component.FindVersion(request.ReleaseId);
        if (version is null)
        {
            return Result.NotFound("发布版本不存在。");
        }

        return component.ComponentKind == ClientReleaseComponentKind.Host
            ? await DeleteHostFilesAsync(component, version, request.Reason, cancellationToken)
            : await DeletePluginFilesAsync(component, version, request.Reason, cancellationToken);
    }

    private async Task<Result<ClientReleaseFileDeletionResultDto>> DeleteHostFilesAsync(
        ClientReleaseComponent component,
        ClientReleaseVersion release,
        string? reason,
        CancellationToken cancellationToken)
    {
        var snapshots = await clientStateStore.GetVersionSnapshotsByDevicesAsync(cancellationToken: cancellationToken);
        var inUse = snapshots.Any(snapshot =>
            string.Equals(snapshot.HostVersion, release.Version, StringComparison.OrdinalIgnoreCase));
        if (inUse)
        {
            const string inUseReason = "已有设备当前宿主版本等于目标版本，禁止物理删除发布文件。";
            await WriteAuditAsync(
                release.Id,
                "Host",
                "Edge Host",
                component.Channel,
                release.Version,
                succeeded: false,
                [],
                [],
                inUseReason,
                cancellationToken);
            return Result.Invalid(inUseReason);
        }

        var edgeRoot = artifactOptions.Value.ResolveEdgeUpdatesRoot();
        var plan = ClientReleaseFileDeletionPlan.ForRelease(edgeRoot, component, release);
        return await ExecuteDeletionAsync(
            component,
            release,
            "Host",
            "Edge Host",
            plan,
            deleteReason => component.MarkVersionDeleted(release.Id, reason ?? deleteReason),
            failure => component.MarkVersionDeleteFailed(release.Id, failure),
            () => componentRepository.SaveChangesAsync(cancellationToken),
            cancellationToken);
    }

    private async Task<Result<ClientReleaseFileDeletionResultDto>> DeletePluginFilesAsync(
        ClientReleaseComponent component,
        ClientReleaseVersion release,
        string? reason,
        CancellationToken cancellationToken)
    {
        var snapshots = await clientStateStore.GetVersionSnapshotsByDevicesAsync(cancellationToken: cancellationToken);
        var inUse = snapshots.Any(snapshot => snapshot.InstalledPlugins.Any(plugin =>
            string.Equals(plugin.ModuleId, component.ComponentKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(plugin.Version, release.Version, StringComparison.OrdinalIgnoreCase)));
        if (inUse)
        {
            const string inUseReason = "已有设备当前插件版本等于目标版本，禁止物理删除发布文件。";
            await WriteAuditAsync(
                release.Id,
                "Plugin",
                component.ComponentKey,
                component.Channel,
                release.Version,
                succeeded: false,
                [],
                [],
                inUseReason,
                cancellationToken);
            return Result.Invalid(inUseReason);
        }

        var edgeRoot = artifactOptions.Value.ResolveEdgeUpdatesRoot();
        var plan = ClientReleaseFileDeletionPlan.ForRelease(edgeRoot, component, release);
        return await ExecuteDeletionAsync(
            component,
            release,
            "Plugin",
            component.ComponentKey,
            plan,
            deleteReason => component.MarkVersionDeleted(release.Id, reason ?? deleteReason),
            failure => component.MarkVersionDeleteFailed(release.Id, failure),
            () => componentRepository.SaveChangesAsync(cancellationToken),
            cancellationToken);
    }

    private async Task<Result<ClientReleaseFileDeletionResultDto>> ExecuteDeletionAsync(
        ClientReleaseComponent component,
        ClientReleaseVersion release,
        string componentKind,
        string componentName,
        ClientReleaseFileDeletionPlan plan,
        Action<string?> markDeleted,
        Action<string> markDeleteFailed,
        Func<Task> saveStatusAsync,
        CancellationToken cancellationToken)
    {
        return await ExecuteDeletionCoreAsync(
            release.Id,
            componentKind,
            componentName,
            component.Channel,
            release.Version,
            markDeleted,
            markDeleteFailed,
            plan,
            saveStatusAsync,
            cancellationToken);
    }

    private async Task<Result<ClientReleaseFileDeletionResultDto>> ExecuteDeletionCoreAsync(
        Guid releaseId,
        string componentKind,
        string componentName,
        string channel,
        string version,
        Action<string?> markDeleted,
        Action<string> markDeleteFailed,
        ClientReleaseFileDeletionPlan plan,
        Func<Task> saveStatusAsync,
        CancellationToken cancellationToken)
    {
        if (plan.Targets.Count == 0)
        {
            const string reason = "发布文件未找到或不在受控发布目录下，已移出可分发 catalog，历史更新内容保留。";
            markDeleted(reason);
            await saveStatusAsync();
            await WriteAuditAsync(
                releaseId,
                componentKind,
                componentName,
                channel,
                version,
                succeeded: true,
                [],
                plan.SkippedPaths,
                reason,
                cancellationToken);
            return Result.Success(new ClientReleaseFileDeletionResultDto(
                releaseId,
                componentKind,
                componentName,
                channel,
                version,
                false,
                [],
                plan.SkippedPaths,
                reason));
        }

        var deletedPaths = new List<string>();
        try
        {
            foreach (var target in plan.Targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                target.AssertSafe();
                deletedPaths.AddRange(target.RelativeFiles);
                target.Delete();
            }

            markDeleted("管理员删除发布包。");
            await saveStatusAsync();

            var warning = plan.SkippedPaths.Count == 0
                ? null
                : $"部分文件仍被 manifest 引用或不在受控范围，已跳过 {plan.SkippedPaths.Count} 项。";
            await WriteAuditAsync(
                releaseId,
                componentKind,
                componentName,
                channel,
                version,
                succeeded: true,
                deletedPaths,
                plan.SkippedPaths,
                warning,
                cancellationToken);

            return Result.Success(new ClientReleaseFileDeletionResultDto(
                releaseId,
                componentKind,
                componentName,
                channel,
                version,
                deletedPaths.Count > 0,
                deletedPaths,
                plan.SkippedPaths,
                warning));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            markDeleteFailed(ex.Message);
            await saveStatusAsync();
            await WriteAuditAsync(
                releaseId,
                componentKind,
                componentName,
                channel,
                version,
                succeeded: false,
                deletedPaths,
                plan.SkippedPaths,
                ex.Message,
                cancellationToken);
            return Result.Invalid($"删除发布包失败: {ex.Message}");
        }
    }

    private async Task WriteAuditAsync(
        Guid releaseId,
        string componentKind,
        string componentName,
        string channel,
        string version,
        bool succeeded,
        IReadOnlyList<string> deletedPaths,
        IReadOnlyList<string> skippedPaths,
        string? failureOrWarning,
        CancellationToken cancellationToken)
    {
        var summary = JsonSerializer.Serialize(new
        {
            action = "ClientRelease.DeletePackage",
            componentKind,
            componentName,
            channel,
            version,
            deletedPaths,
            skippedPaths
        });

        await auditTrailService.TryWriteAsync(
            new AuditTrailEntry(
                ClientReleaseAuditActor.ParseId(currentUser.Id),
                currentUser.UserName,
                "ClientRelease.DeletePackage",
                "ClientRelease",
                releaseId.ToString(),
                DateTime.UtcNow,
                succeeded,
                summary,
                succeeded ? null : failureOrWarning),
            cancellationToken);
    }

}

internal sealed class ClientReleaseFileDeletionPlan
{
    private ClientReleaseFileDeletionPlan(
        IReadOnlyList<ClientReleaseFileDeletionTarget> targets,
        IReadOnlyList<string> skippedPaths)
    {
        Targets = targets;
        SkippedPaths = skippedPaths;
    }

    public IReadOnlyList<ClientReleaseFileDeletionTarget> Targets { get; }

    public IReadOnlyList<string> SkippedPaths { get; }

    public static ClientReleaseFileDeletionPlan ForRelease(
        string edgeRoot,
        ClientReleaseComponent component,
        ClientReleaseVersion release)
    {
        var targets = new List<ClientReleaseFileDeletionTarget>();
        var skipped = new List<string>();
        foreach (var artifact in release.Artifacts)
        {
            var fullPath = Path.Combine(edgeRoot, artifact.RelativePath);
            switch (artifact.ArtifactKind)
            {
                case ClientReleaseArtifactKind.InstallerDirectory:
                case ClientReleaseArtifactKind.PluginPackageDirectory:
                    TryAddDirectory(targets, skipped, edgeRoot, fullPath);
                    break;
                case ClientReleaseArtifactKind.ManifestFile:
                case ClientReleaseArtifactKind.PackageFile:
                    TryAddFile(targets, skipped, edgeRoot, fullPath);
                    break;
                case ClientReleaseArtifactKind.VelopackFile:
                    TryAddVelopackFile(targets, skipped, edgeRoot, component.Channel, fullPath);
                    break;
                default:
                    skipped.Add(artifact.RelativePath);
                    break;
            }
        }

        return new ClientReleaseFileDeletionPlan(targets, skipped);
    }

    private static void TryAddVelopackFile(
        ICollection<ClientReleaseFileDeletionTarget> targets,
        ICollection<string> skipped,
        string edgeRoot,
        string channel,
        string path)
    {
        var velopackRoot = Path.Combine(edgeRoot, "velopack", channel);
        var name = Path.GetFileName(path);
        if (!Directory.Exists(velopackRoot) || !File.Exists(path))
        {
            return;
        }

        if (ClientReleaseVelopackPaths.IsProtectedChannelManifest(name))
        {
            skipped.Add(ToRelative(edgeRoot, path));
            return;
        }

        var manifestPaths = Directory.EnumerateFiles(
                velopackRoot,
                "*",
                SearchOption.TopDirectoryOnly)
            .Where(candidate => ClientReleaseVelopackPaths.IsProtectedChannelManifest(
                Path.GetFileName(candidate)));
        if (ClientReleaseVelopackPaths.IsReferencedByManifests(manifestPaths, name))
        {
            skipped.Add(ToRelative(edgeRoot, path));
            return;
        }

        TryAddFile(targets, skipped, edgeRoot, path);
    }

    private static void TryAddDirectory(
        ICollection<ClientReleaseFileDeletionTarget> targets,
        ICollection<string> skipped,
        string edgeRoot,
        string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        targets.Add(ClientReleaseFileDeletionTarget.Directory(edgeRoot, path));
    }

    private static void TryAddFile(
        ICollection<ClientReleaseFileDeletionTarget> targets,
        ICollection<string> skipped,
        string edgeRoot,
        string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        targets.Add(ClientReleaseFileDeletionTarget.File(edgeRoot, path));
    }

    private static string ToRelative(string edgeRoot, string path)
        => Path.GetRelativePath(edgeRoot, path).Replace('\\', '/');
}

internal sealed class ClientReleaseFileDeletionTarget
{
    private ClientReleaseFileDeletionTarget(
        string edgeRoot,
        string path,
        bool isDirectory)
    {
        EdgeRoot = Path.GetFullPath(edgeRoot);
        PathToDelete = Path.GetFullPath(path);
        IsDirectory = isDirectory;
        RelativeFiles = ResolveRelativeFiles(EdgeRoot, PathToDelete, isDirectory);
    }

    public string EdgeRoot { get; }

    public string PathToDelete { get; }

    public bool IsDirectory { get; }

    public IReadOnlyList<string> RelativeFiles { get; }

    public static ClientReleaseFileDeletionTarget Directory(string edgeRoot, string path)
        => new(edgeRoot, path, isDirectory: true);

    public static ClientReleaseFileDeletionTarget File(string edgeRoot, string path)
        => new(edgeRoot, path, isDirectory: false);

    public void AssertSafe()
    {
        var root = EnsureTrailingSeparator(EdgeRoot);
        var target = Path.GetFullPath(PathToDelete);
        if (!target.StartsWith(root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("发布文件路径越过受控发布目录。");
        }

        if (target.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("发布文件路径包含非法上级目录片段。");
        }

        AssertNoReparsePoint(target, IsDirectory);
    }

    public void Delete()
    {
        if (IsDirectory)
        {
            if (System.IO.Directory.Exists(PathToDelete))
            {
                System.IO.Directory.Delete(PathToDelete, recursive: true);
            }

            return;
        }

        if (System.IO.File.Exists(PathToDelete))
        {
            System.IO.File.Delete(PathToDelete);
        }
    }

    private static IReadOnlyList<string> ResolveRelativeFiles(
        string edgeRoot,
        string path,
        bool isDirectory)
    {
        if (isDirectory)
        {
            return System.IO.Directory.Exists(path)
                ? System.IO.Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Select(file => ToRelative(edgeRoot, file))
                    .OrderBy(file => file, StringComparer.Ordinal)
                    .ToList()
                : [];
        }

        return System.IO.File.Exists(path) ? [ToRelative(edgeRoot, path)] : [];
    }

    private static void AssertNoReparsePoint(string path, bool isDirectory)
    {
        if (IsReparsePoint(path))
        {
            throw new InvalidOperationException("发布文件路径包含符号链接或重解析点。");
        }

        if (!isDirectory || !System.IO.Directory.Exists(path))
        {
            return;
        }

        foreach (var entry in System.IO.Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
        {
            if (IsReparsePoint(entry))
            {
                throw new InvalidOperationException("发布文件目录中包含符号链接或重解析点。");
            }
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (System.IO.File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static string ToRelative(string edgeRoot, string path)
        => System.IO.Path.GetRelativePath(edgeRoot, path).Replace('\\', '/');

    private static string EnsureTrailingSeparator(string path)
    {
        var separator = System.IO.Path.DirectorySeparatorChar;
        return path.EndsWith(separator)
            ? path
            : $"{path}{separator}";
    }
}
