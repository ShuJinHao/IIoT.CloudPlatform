using IIoT.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace IIoT.ProductionService.ClientReleases;

internal sealed record VelopackPublishedFile(
    string RelativePath,
    string TargetPath,
    string Sha256,
    long Size);

internal sealed class VelopackReleasePublishFileTransaction
{
    private readonly string edgeRoot;
    private readonly string sourceRoot;
    private readonly string targetRoot;
    private readonly string backupRoot;
    private readonly ILogger logger;
    private readonly List<VelopackFileChange> changes = [];
    private readonly List<string> createdDirectories = [];
    private readonly List<VelopackPublishedFile> publishedFiles = [];

    public VelopackReleasePublishFileTransaction(
        string edgeRoot,
        string sourceRoot,
        string targetRoot,
        string backupRoot,
        ILogger logger)
    {
        this.edgeRoot = Path.GetFullPath(edgeRoot);
        this.sourceRoot = Path.GetFullPath(sourceRoot);
        this.targetRoot = Path.GetFullPath(targetRoot);
        this.backupRoot = Path.GetFullPath(backupRoot);
        this.logger = logger;

        ClientReleaseControlledDirectory.ValidateChain(
            this.edgeRoot,
            this.sourceRoot,
            "Velopack 发布路径非法。",
            requireStrictChild: true);
        ClientReleaseControlledDirectory.ValidateChain(
            this.edgeRoot,
            this.targetRoot,
            "Velopack 发布路径非法。",
            requireStrictChild: true);
        ClientReleaseControlledDirectory.ValidateChain(
            this.edgeRoot,
            this.backupRoot,
            "Velopack 发布路径非法。",
            requireStrictChild: true);
    }

    public string TargetRoot => targetRoot;

    public IReadOnlyList<VelopackPublishedFile> PublishedFiles => publishedFiles;

    public void Publish()
    {
        if (!ClientReleaseControlledDirectory.IsExistingDirectory(edgeRoot, sourceRoot))
        {
            throw new ClientReleaseValidationException("Velopack 发布源目录无效。");
        }

        ClientReleaseControlledDirectory.EnsureExists(
            edgeRoot,
            targetRoot,
            createdDirectories,
            "Velopack 发布目录非法。");
        var sourceFiles = ClientReleaseDirectorySnapshot.Capture(sourceRoot).Files
            .Select(file => new SourceFile(
                Path.Combine(sourceRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
                file.RelativePath,
                new ClientReleaseFileFact(file.Sha256, file.Size)))
            .OrderBy(file => IsNugetPackage(file.RelativePath) ? 0 : 1)
            .ThenBy(file => ClientReleaseVelopackPaths.IsChannelManifest(file.RelativePath) ? 1 : 0)
            .ThenBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();

        foreach (var sourceFile in sourceFiles)
        {
            PublishFile(sourceFile.Path, sourceFile.RelativePath, sourceFile.Fact);
        }

        if (!sourceFiles.Any(file =>
                string.Equals(file.RelativePath, "RELEASES", StringComparison.OrdinalIgnoreCase)))
        {
            var channelReleases = sourceFiles
                .Where(file => Path.GetDirectoryName(file.RelativePath) is null or ""
                               && Path.GetFileName(file.RelativePath)
                                   .StartsWith("RELEASES-", StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                .FirstOrDefault();
            if (channelReleases is not null)
            {
                PublishFile(channelReleases.Path, "RELEASES", channelReleases.Fact);
            }
        }
    }

    public bool HasExactPublishedFiles()
    {
        try
        {
            return publishedFiles.Count > 0
                   && publishedFiles.All(file =>
                       ClientReleaseControlledDirectory.IsExistingDirectory(
                           edgeRoot,
                           Path.GetDirectoryName(file.TargetPath)!)
                       && ClientReleaseFileFacts.IsExactRegularFile(
                           file.TargetPath,
                           file.Sha256,
                           file.Size));
        }
        catch (Exception ex)
        {
            ClientReleasePublishDiagnostics.LogFailure(
                logger,
                LogLevel.Warning,
                ClientReleasePublishDiagnostics.HostCommitUnknown,
                "host-velopack-static-observation",
                ex,
                "host-velopack");
            return false;
        }
    }

    public bool TryRollbackBeforeSave()
    {
        var succeeded = true;
        foreach (var change in changes.AsEnumerable().Reverse())
        {
            try
            {
                if (!ClientReleaseControlledDirectory.IsExistingDirectory(
                        edgeRoot,
                        Path.GetDirectoryName(change.TargetPath)!)
                    || !ClientReleaseFileFacts.IsExactRegularFile(
                        change.TargetPath,
                        change.Published.Sha256,
                        change.Published.Size))
                {
                    ClientReleasePublishDiagnostics.LogCondition(
                        logger,
                        ClientReleasePublishDiagnostics.HostRollbackOwnershipMismatch,
                        "host-velopack-rollback",
                        "published-file-changed",
                        change.RelativePath);
                    succeeded = false;
                    continue;
                }

                if (change.Previous is null)
                {
                    File.Delete(change.TargetPath);
                    continue;
                }

                if (change.BackupPath is null
                    || !ClientReleaseControlledDirectory.IsExistingDirectory(
                        edgeRoot,
                        Path.GetDirectoryName(change.BackupPath)!)
                    || !ClientReleaseFileFacts.IsExactRegularFile(
                        change.BackupPath,
                        change.Previous.Sha256,
                        change.Previous.Size))
                {
                    ClientReleasePublishDiagnostics.LogCondition(
                        logger,
                        ClientReleasePublishDiagnostics.HostRollbackOwnershipMismatch,
                        "host-velopack-rollback",
                        "exact-backup-unavailable",
                        change.RelativePath);
                    succeeded = false;
                    continue;
                }

                var restorePath = CreatePrivateSiblingPath(change.TargetPath, "restore");
                try
                {
                    File.Copy(change.BackupPath, restorePath, overwrite: false);
                    if (!ClientReleaseFileFacts.IsExactRegularFile(
                            restorePath,
                            change.Previous.Sha256,
                            change.Previous.Size))
                    {
                        throw new InvalidDataException("Velopack rollback backup changed while restoring.");
                    }

                    File.Move(restorePath, change.TargetPath, overwrite: true);
                    if (!ClientReleaseFileFacts.IsExactRegularFile(
                            change.TargetPath,
                            change.Previous.Sha256,
                            change.Previous.Size))
                    {
                        throw new InvalidDataException("Velopack rollback restore verification failed.");
                    }
                }
                finally
                {
                    TryDeletePrivateFile(restorePath);
                }
            }
            catch (Exception ex)
            {
                ClientReleasePublishDiagnostics.LogFailure(
                    logger,
                    LogLevel.Warning,
                    ClientReleasePublishDiagnostics.HostRollbackCleanupFailed,
                    "host-velopack-rollback",
                    ex,
                    change.RelativePath);
                succeeded = false;
            }
        }

        foreach (var directory in createdDirectories
                     .OrderByDescending(path => path.Length)
                     .ThenByDescending(path => path, StringComparer.Ordinal))
        {
            try
            {
                if (ClientReleaseControlledDirectory.IsExistingDirectory(edgeRoot, directory)
                    && !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory, recursive: false);
                }
            }
            catch (Exception ex)
            {
                ClientReleasePublishDiagnostics.LogFailure(
                    logger,
                    LogLevel.Warning,
                    ClientReleasePublishDiagnostics.HostRollbackCleanupFailed,
                    "host-velopack-empty-directory-cleanup",
                    ex,
                    "host-velopack");
                succeeded = false;
            }
        }

        return succeeded;
    }

    private void PublishFile(
        string sourcePath,
        string relativePath,
        ClientReleaseFileFact publishedFact)
    {
        ClientReleaseControlledDirectory.ValidateChain(
            edgeRoot,
            Path.GetDirectoryName(sourcePath)!,
            "Velopack 发布源目录无效。");
        if (!ClientReleaseFileFacts.IsExactRegularFile(
                sourcePath,
                publishedFact.Sha256,
                publishedFact.Size))
        {
            throw new ClientReleaseValidationException("Velopack 发布源文件已变化。");
        }

        var targetPath = Path.GetFullPath(Path.Combine(
            targetRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        ClientReleaseControlledDirectory.EnsureExists(
            edgeRoot,
            Path.GetDirectoryName(targetPath)!,
            createdDirectories,
            "Velopack 发布目录非法。");
        if (IsNugetPackage(relativePath) && File.Exists(targetPath))
        {
            if (!ClientReleaseFileFacts.IsExactRegularFile(
                    targetPath,
                    publishedFact.Sha256,
                    publishedFact.Size))
            {
                throw new ClientReleasePublishConflictException();
            }

            publishedFiles.Add(new VelopackPublishedFile(
                relativePath,
                targetPath,
                publishedFact.Sha256,
                publishedFact.Size));
            return;
        }

        ClientReleaseFileFact? previous = null;
        string? backupPath = null;
        if (File.Exists(targetPath))
        {
            previous = ClientReleaseFileFacts.GetFileFact(targetPath);
            backupPath = Path.GetFullPath(Path.Combine(
                backupRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            ClientReleaseControlledDirectory.EnsureExists(
                edgeRoot,
                Path.GetDirectoryName(backupPath)!,
                createdDirectories,
                "Velopack 备份目录非法。");
            File.Copy(targetPath, backupPath, overwrite: false);
            if (!ClientReleaseFileFacts.IsExactRegularFile(
                    backupPath,
                    previous.Sha256,
                    previous.Size))
            {
                throw new InvalidDataException("Velopack backup verification failed.");
            }
        }

        var privatePath = CreatePrivateSiblingPath(targetPath, "publish");
        try
        {
            File.Copy(sourcePath, privatePath, overwrite: false);
            if (!ClientReleaseFileFacts.IsExactRegularFile(
                    privatePath,
                    publishedFact.Sha256,
                    publishedFact.Size))
            {
                throw new InvalidDataException("Velopack publish copy verification failed.");
            }

            File.Move(privatePath, targetPath, overwrite: previous is not null);
            changes.Add(new VelopackFileChange(
                relativePath,
                targetPath,
                publishedFact,
                previous,
                backupPath));
        }
        finally
        {
            TryDeletePrivateFile(privatePath);
        }

        publishedFiles.Add(new VelopackPublishedFile(
            relativePath,
            targetPath,
            publishedFact.Sha256,
            publishedFact.Size));
    }

    private static bool IsNugetPackage(string relativePath)
        => relativePath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);

    private static string CreatePrivateSiblingPath(string targetPath, string purpose)
        => Path.Combine(
            Path.GetDirectoryName(targetPath)!,
            $".{Path.GetFileName(targetPath)}.{purpose}-{Guid.NewGuid():N}");

    private static void TryDeletePrivateFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record SourceFile(
        string Path,
        string RelativePath,
        ClientReleaseFileFact Fact);

    private sealed record VelopackFileChange(
        string RelativePath,
        string TargetPath,
        ClientReleaseFileFact Published,
        ClientReleaseFileFact? Previous,
        string? BackupPath);
}

internal static class ClientReleaseVelopackPaths
{
    public static bool IsChannelManifest(string relativePath)
        => relativePath.Equals("releases.stable.json", StringComparison.OrdinalIgnoreCase)
           || relativePath.Equals("assets.stable.json", StringComparison.OrdinalIgnoreCase)
           || relativePath.Equals("RELEASES", StringComparison.OrdinalIgnoreCase)
           || relativePath.StartsWith("RELEASES-", StringComparison.OrdinalIgnoreCase);
}
