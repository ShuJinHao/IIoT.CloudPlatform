using IIoT.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace IIoT.ProductionService.ClientReleases;

internal sealed class PluginReleasePublishFileTransaction
{
    private const string OwnershipMarkerFileName = ".iiot-plugin-publish-owner";
    private readonly string edgeRoot;
    private readonly string targetDirectory;
    private readonly string targetPackagePath;
    private readonly string targetOwnershipMarkerPath;
    private readonly string privateDirectory;
    private readonly string privatePackagePath;
    private readonly string privateOwnershipMarkerPath;
    private readonly string ownershipToken = ClientReleaseOwnershipMarker.CreateToken();
    private readonly string expectedSha256;
    private readonly long expectedSize;
    private readonly ILogger logger;
    private readonly Action<string, string> writeOwnershipMarker;
    private bool privateDirectoryCreated;
    private bool targetOwned;
    private bool ownershipMarkerRemoved;

    public PluginReleasePublishFileTransaction(
        string edgeRoot,
        string targetDirectory,
        string packageFileName,
        string expectedSha256,
        long expectedSize,
        ILogger logger,
        Action<string, string>? writeOwnershipMarker = null)
    {
        this.edgeRoot = Path.GetFullPath(edgeRoot);
        this.targetDirectory = Path.GetFullPath(targetDirectory);
        targetPackagePath = Path.GetFullPath(Path.Combine(this.targetDirectory, packageFileName));
        targetOwnershipMarkerPath = Path.Combine(this.targetDirectory, OwnershipMarkerFileName);
        var targetParent = Path.GetDirectoryName(this.targetDirectory)!;
        privateDirectory = Path.Combine(targetParent, $".plugin-publish-{Guid.NewGuid():N}");
        privatePackagePath = Path.Combine(privateDirectory, packageFileName);
        privateOwnershipMarkerPath = Path.Combine(privateDirectory, OwnershipMarkerFileName);
        this.expectedSha256 = expectedSha256;
        this.expectedSize = expectedSize;
        this.logger = logger;
        this.writeOwnershipMarker = writeOwnershipMarker ?? ClientReleaseOwnershipMarker.Write;

        if (!string.Equals(Path.GetDirectoryName(targetPackagePath), this.targetDirectory, StringComparison.Ordinal)
            || !string.Equals(Path.GetFileName(targetPackagePath), packageFileName, StringComparison.Ordinal))
        {
            throw new ClientReleaseValidationException("Edge 插件发布目录非法。");
        }

        ClientReleaseControlledDirectory.ValidateChain(
            this.edgeRoot,
            this.targetDirectory,
            "Edge 插件发布目录非法。",
            requireStrictChild: true);
        ClientReleaseControlledDirectory.ValidateChain(
            this.edgeRoot,
            privateDirectory,
            "Edge 插件发布目录非法。",
            requireStrictChild: true);
    }

    public string TargetPackagePath => targetPackagePath;

    public void Publish(string sourcePackagePath)
    {
        var fullSourcePackagePath = Path.GetFullPath(sourcePackagePath);
        ClientReleaseControlledDirectory.ValidateChain(
            edgeRoot,
            Path.GetDirectoryName(fullSourcePackagePath)!,
            "Edge 插件暂存包非法。");
        if (!ClientReleaseFileFacts.IsExactRegularFile(
                fullSourcePackagePath,
                expectedSha256,
                expectedSize))
        {
            throw new ClientReleaseValidationException("Edge 插件暂存包非法。");
        }

        ClientReleaseControlledDirectory.EnsureExists(
            edgeRoot,
            Path.GetDirectoryName(privateDirectory)!,
            createdDirectories: null,
            "Edge 插件发布目录非法。");
        ClientReleaseControlledDirectory.EnsureExists(
            edgeRoot,
            privateDirectory,
            createdDirectories: null,
            "Edge 插件发布目录非法。");
        privateDirectoryCreated = true;

        writeOwnershipMarker(privateOwnershipMarkerPath, ownershipToken);

        File.Move(fullSourcePackagePath, privatePackagePath, overwrite: false);
        try
        {
            Directory.Move(privateDirectory, targetDirectory);
            privateDirectoryCreated = false;
            targetOwned = true;
        }
        catch (IOException) when (Directory.Exists(targetDirectory) || File.Exists(targetDirectory))
        {
            TryDeletePrivateDirectory();
            throw new ClientReleasePublishConflictException();
        }
    }

    public bool HasExactPublishedPackage()
    {
        try
        {
            return targetOwned
                   && (ownershipMarkerRemoved
                       ? ValidatePublishedTargetAfterMarkerRemoval()
                       : ValidateOwnedTargetDirectory());
        }
        catch (Exception ex)
        {
            ClientReleasePublishDiagnostics.LogFailure(
                logger,
                LogLevel.Warning,
                ClientReleasePublishDiagnostics.PluginCommitUnknown,
                "plugin-package-static-observation",
                ex,
                "plugin-release");
            return false;
        }
    }

    public bool TryRollbackBeforeSave()
    {
        if (!targetOwned)
        {
            return TryDeletePrivateDirectory();
        }

        try
        {
            if (!ValidateOwnedTargetDirectory())
            {
                ClientReleasePublishDiagnostics.LogCondition(
                    logger,
                    ClientReleasePublishDiagnostics.PluginRollbackOwnershipMismatch,
                    "plugin-rollback-ownership-check",
                    "owned-directory-validation-failed",
                    "plugin-release");
                return false;
            }

            DeleteKnownOwnedDirectory(
                targetDirectory,
                targetOwnershipMarkerPath,
                targetPackagePath);
            targetOwned = false;
            return true;
        }
        catch (Exception ex)
        {
            ClientReleasePublishDiagnostics.LogFailure(
                logger,
                LogLevel.Warning,
                ClientReleasePublishDiagnostics.PluginRollbackCleanupFailed,
                "plugin-rollback-owned-directory-delete",
                ex,
                "plugin-release");
            return false;
        }
    }

    public bool TryRemoveOwnershipMarker()
    {
        if (ownershipMarkerRemoved)
        {
            return true;
        }

        try
        {
            if (!targetOwned
                || !ClientReleaseOwnershipMarker.Matches(targetOwnershipMarkerPath, ownershipToken))
            {
                ClientReleasePublishDiagnostics.LogCondition(
                    logger,
                    ClientReleasePublishDiagnostics.PluginOwnershipMarkerCleanupFailed,
                    "plugin-owner-marker-cleanup",
                    "owner-marker-not-current",
                    "plugin-release");
                return false;
            }

            File.Delete(targetOwnershipMarkerPath);
            ownershipMarkerRemoved = true;
            return true;
        }
        catch (Exception ex)
        {
            ClientReleasePublishDiagnostics.LogFailure(
                logger,
                LogLevel.Warning,
                ClientReleasePublishDiagnostics.PluginOwnershipMarkerCleanupFailed,
                "plugin-owner-marker-cleanup",
                ex,
                "plugin-release");
            return false;
        }
    }

    private bool ValidateOwnedTargetDirectory()
    {
        return IsControlledDirectory(targetDirectory, targetPackagePath)
               && HasExactEntries(targetDirectory, targetOwnershipMarkerPath, targetPackagePath)
               && ClientReleaseOwnershipMarker.Matches(targetOwnershipMarkerPath, ownershipToken)
               && ClientReleaseFileFacts.IsExactRegularFile(
                   targetPackagePath,
                   expectedSha256,
                   expectedSize);
    }

    private bool ValidatePublishedTargetAfterMarkerRemoval()
    {
        return IsControlledDirectory(targetDirectory, targetPackagePath)
               && HasExactEntries(targetDirectory, targetPackagePath)
               && ClientReleaseFileFacts.IsExactRegularFile(
                   targetPackagePath,
                   expectedSha256,
                   expectedSize);
    }

    private bool TryDeletePrivateDirectory()
    {
        if (!privateDirectoryCreated || !Directory.Exists(privateDirectory))
        {
            return true;
        }

        try
        {
            if (!Directory.EnumerateFileSystemEntries(privateDirectory).Any())
            {
                Directory.Delete(privateDirectory);
                privateDirectoryCreated = false;
                return true;
            }

            if (!ValidateOwnedPrivateDirectory())
            {
                ClientReleasePublishDiagnostics.LogCondition(
                    logger,
                    ClientReleasePublishDiagnostics.PluginRollbackOwnershipMismatch,
                    "plugin-private-staging-cleanup",
                    "private-directory-validation-failed",
                    "plugin-release");
                return false;
            }

            DeleteKnownOwnedDirectory(
                privateDirectory,
                privateOwnershipMarkerPath,
                privatePackagePath);
            privateDirectoryCreated = false;
            return true;
        }
        catch (Exception ex)
        {
            ClientReleasePublishDiagnostics.LogFailure(
                logger,
                LogLevel.Warning,
                ClientReleasePublishDiagnostics.PluginRollbackCleanupFailed,
                "plugin-private-staging-cleanup",
                ex,
                "plugin-release");
            return false;
        }
    }

    private bool ValidateOwnedPrivateDirectory()
    {
        if (!IsControlledDirectory(privateDirectory, privatePackagePath)
            || !ClientReleaseOwnershipMarker.Matches(privateOwnershipMarkerPath, ownershipToken))
        {
            return false;
        }

        if (!HasExactEntries(
                privateDirectory,
                File.Exists(privatePackagePath)
                    ? [privateOwnershipMarkerPath, privatePackagePath]
                    : [privateOwnershipMarkerPath]))
        {
            return false;
        }

        return !File.Exists(privatePackagePath)
               || ClientReleaseFileFacts.IsExactRegularFile(
                   privatePackagePath,
                   expectedSha256,
                   expectedSize);
    }

    private bool IsControlledDirectory(string directory, string packagePath)
    {
        return ClientReleaseControlledDirectory.IsExistingDirectory(edgeRoot, directory)
               && string.Equals(Path.GetDirectoryName(packagePath), directory, StringComparison.Ordinal);
    }

    private static bool HasExactEntries(string directory, params string[] expectedPaths)
    {
        var entries = Directory.EnumerateFileSystemEntries(
                directory,
                "*",
                SearchOption.TopDirectoryOnly)
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var expectedEntries = expectedPaths
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        return entries.SequenceEqual(expectedEntries, StringComparer.Ordinal);
    }

    private static void DeleteKnownOwnedDirectory(
        string directory,
        string markerPath,
        string packagePath)
    {
        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        if (File.Exists(markerPath))
        {
            File.Delete(markerPath);
        }

        Directory.Delete(directory, recursive: false);
    }

}
