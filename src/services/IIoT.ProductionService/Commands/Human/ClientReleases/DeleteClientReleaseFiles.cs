using System.Text.Json;
using IIoT.Core.Production.Aggregates.ClientReleases;
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
public sealed record DeleteClientReleaseFilesCommand(Guid ReleaseId)
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

public sealed class DeleteClientReleaseFilesHandler(
    IOptions<EdgeInstallerArtifactOptions> artifactOptions,
    IRepository<ClientHostRelease> hostRepository,
    IRepository<ClientPluginRelease> pluginRepository,
    IReadRepository<DeviceClientVersionSnapshot> snapshotRepository,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService)
    : ICommandHandler<DeleteClientReleaseFilesCommand, Result<ClientReleaseFileDeletionResultDto>>
{
    public async Task<Result<ClientReleaseFileDeletionResultDto>> Handle(
        DeleteClientReleaseFilesCommand request,
        CancellationToken cancellationToken)
    {
        var host = await hostRepository.GetSingleOrDefaultAsync(
            new ClientHostReleaseByIdSpec(request.ReleaseId),
            cancellationToken);
        if (host is not null)
        {
            return await DeleteHostFilesAsync(host, cancellationToken);
        }

        var plugin = await pluginRepository.GetSingleOrDefaultAsync(
            new ClientPluginReleaseByIdSpec(request.ReleaseId),
            cancellationToken);
        if (plugin is not null)
        {
            return await DeletePluginFilesAsync(plugin, cancellationToken);
        }

        return Result.NotFound("发布版本不存在。");
    }

    private async Task<Result<ClientReleaseFileDeletionResultDto>> DeleteHostFilesAsync(
        ClientHostRelease release,
        CancellationToken cancellationToken)
    {
        var snapshots = await snapshotRepository.GetListAsync(
            new DeviceClientVersionSnapshotsByDevicesSpec(),
            cancellationToken);
        var inUse = snapshots.Any(snapshot =>
            string.Equals(snapshot.HostVersion, release.Version, StringComparison.OrdinalIgnoreCase));
        if (inUse)
        {
            const string reason = "已有设备当前宿主版本等于目标版本，禁止物理删除发布文件。";
            await WriteAuditAsync(
                release.Id,
                "Host",
                "Edge Host",
                release.Channel,
                release.Version,
                succeeded: false,
                [],
                [],
                reason,
                cancellationToken);
            return Result.Invalid(reason);
        }

        var edgeRoot = ResolveEdgeUpdatesRoot();
        var plan = ClientReleaseFileDeletionPlan.ForHost(edgeRoot, release);
        return await ExecuteDeletionAsync(
            release,
            "Host",
            "Edge Host",
            plan,
            () => hostRepository.SaveChangesAsync(cancellationToken),
            cancellationToken);
    }

    private async Task<Result<ClientReleaseFileDeletionResultDto>> DeletePluginFilesAsync(
        ClientPluginRelease release,
        CancellationToken cancellationToken)
    {
        var snapshots = await snapshotRepository.GetListAsync(
            new DeviceClientVersionSnapshotsByDevicesSpec(),
            cancellationToken);
        var inUse = snapshots.Any(snapshot => snapshot.InstalledPlugins.Any(plugin =>
            string.Equals(plugin.ModuleId, release.ModuleId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(plugin.Version, release.Version, StringComparison.OrdinalIgnoreCase)));
        if (inUse)
        {
            const string reason = "已有设备当前插件版本等于目标版本，禁止物理删除发布文件。";
            await WriteAuditAsync(
                release.Id,
                "Plugin",
                release.ModuleId,
                release.Channel,
                release.Version,
                succeeded: false,
                [],
                [],
                reason,
                cancellationToken);
            return Result.Invalid(reason);
        }

        var edgeRoot = ResolveEdgeUpdatesRoot();
        var plan = ClientReleaseFileDeletionPlan.ForPlugin(edgeRoot, release);
        return await ExecuteDeletionAsync(
            release,
            "Plugin",
            release.ModuleId,
            plan,
            () => pluginRepository.SaveChangesAsync(cancellationToken),
            cancellationToken);
    }

    private async Task<Result<ClientReleaseFileDeletionResultDto>> ExecuteDeletionAsync(
        ClientHostRelease release,
        string componentKind,
        string componentName,
        ClientReleaseFileDeletionPlan plan,
        Func<Task> saveStatusAsync,
        CancellationToken cancellationToken)
    {
        return await ExecuteDeletionCoreAsync(
            release.Id,
            componentKind,
            componentName,
            release.Channel,
            release.Version,
            () => release.ChangeStatus(ClientReleaseStatus.Archived),
            plan,
            saveStatusAsync,
            cancellationToken);
    }

    private async Task<Result<ClientReleaseFileDeletionResultDto>> ExecuteDeletionAsync(
        ClientPluginRelease release,
        string componentKind,
        string componentName,
        ClientReleaseFileDeletionPlan plan,
        Func<Task> saveStatusAsync,
        CancellationToken cancellationToken)
    {
        return await ExecuteDeletionCoreAsync(
            release.Id,
            componentKind,
            componentName,
            release.Channel,
            release.Version,
            () => release.ChangeStatus(ClientReleaseStatus.Archived),
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
        Action archiveRelease,
        ClientReleaseFileDeletionPlan plan,
        Func<Task> saveStatusAsync,
        CancellationToken cancellationToken)
    {
        if (plan.Targets.Count == 0)
        {
            const string reason = "发布文件不在本机 /edge-updates 受控目录下，无法执行物理删除。";
            await WriteAuditAsync(
                releaseId,
                componentKind,
                componentName,
                channel,
                version,
                succeeded: false,
                [],
                plan.SkippedPaths,
                reason,
                cancellationToken);
            return Result.Invalid(reason);
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

            archiveRelease();
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
            return Result.Invalid($"删除发布文件失败: {ex.Message}");
        }
    }

    private string ResolveEdgeUpdatesRoot()
    {
        var installerRoot = Path.GetFullPath(artifactOptions.Value.RootPath);
        var parent = Directory.GetParent(installerRoot);
        if (parent is null)
        {
            throw new InvalidOperationException("EdgeInstallerArtifacts:RootPath 必须位于 edge-updates/installers 下。");
        }

        return parent.FullName;
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
            action = "ClientRelease.DeleteFiles",
            componentKind,
            componentName,
            channel,
            version,
            deletedPaths,
            skippedPaths
        });

        await auditTrailService.TryWriteAsync(
            new AuditTrailEntry(
                ParseActorUserId(currentUser.Id),
                currentUser.UserName,
                "ClientRelease.DeleteFiles",
                "ClientRelease",
                releaseId.ToString(),
                DateTime.UtcNow,
                succeeded,
                summary,
                succeeded ? null : failureOrWarning),
            cancellationToken);
    }

    private static Guid? ParseActorUserId(string? userId)
        => Guid.TryParse(userId, out var parsed) ? parsed : null;
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

    public static ClientReleaseFileDeletionPlan ForHost(string edgeRoot, ClientHostRelease release)
    {
        var targets = new List<ClientReleaseFileDeletionTarget>();
        var skipped = new List<string>();
        TryAddDirectory(targets, skipped, edgeRoot, Path.Combine(edgeRoot, "installers", release.Channel, release.Version));
        AddVelopackTargets(targets, skipped, edgeRoot, release.Channel, release.Version);
        return new ClientReleaseFileDeletionPlan(targets, skipped);
    }

    public static ClientReleaseFileDeletionPlan ForPlugin(string edgeRoot, ClientPluginRelease release)
    {
        var targets = new List<ClientReleaseFileDeletionTarget>();
        var skipped = new List<string>();
        TryAddDirectory(
            targets,
            skipped,
            edgeRoot,
            Path.Combine(edgeRoot, "plugins", release.Channel, EscapeFileSystemSegment(release.ModuleId), release.Version));
        return new ClientReleaseFileDeletionPlan(targets, skipped);
    }

    private static void AddVelopackTargets(
        ICollection<ClientReleaseFileDeletionTarget> targets,
        ICollection<string> skipped,
        string edgeRoot,
        string channel,
        string version)
    {
        var velopackRoot = Path.Combine(edgeRoot, "velopack", channel);
        if (!Directory.Exists(velopackRoot))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(velopackRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (IsVelopackChannelManifest(name))
            {
                skipped.Add(ToRelative(edgeRoot, file));
                continue;
            }

            if (!FileNameContainsVersion(name, version))
            {
                continue;
            }

            if (VelopackManifestsReferenceFile(velopackRoot, name))
            {
                skipped.Add(ToRelative(edgeRoot, file));
                continue;
            }

            TryAddFile(targets, skipped, edgeRoot, file);
        }
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

    private static bool IsVelopackChannelManifest(string fileName)
        => fileName.StartsWith("releases.", StringComparison.OrdinalIgnoreCase)
           || fileName.StartsWith("assets.", StringComparison.OrdinalIgnoreCase)
           || string.Equals(fileName, "RELEASES", StringComparison.OrdinalIgnoreCase);

    private static bool VelopackManifestsReferenceFile(string velopackChannelRoot, string fileName)
    {
        foreach (var manifestPath in Directory.EnumerateFiles(velopackChannelRoot, "*", SearchOption.TopDirectoryOnly)
                     .Where(path => IsVelopackChannelManifest(Path.GetFileName(path))))
        {
            if (File.ReadAllText(manifestPath).Contains(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool FileNameContainsVersion(string fileName, string version)
    {
        var pattern = $@"(^|[._-]){System.Text.RegularExpressions.Regex.Escape(version)}([._-]|$)";
        return System.Text.RegularExpressions.Regex.IsMatch(
            fileName,
            pattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static string EscapeFileSystemSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim()
            .Select(ch => invalid.Contains(ch) ? '_' : ch)
            .ToArray();
        return new string(chars);
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
            throw new InvalidOperationException("发布文件路径越过 /edge-updates 受控目录。");
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
