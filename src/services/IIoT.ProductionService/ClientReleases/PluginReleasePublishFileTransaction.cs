using System.Text;
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
    private readonly string ownershipToken = Guid.NewGuid().ToString("N");
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
        this.writeOwnershipMarker = writeOwnershipMarker ?? WriteOwnershipMarker;

        if (!ClientReleaseFileFacts.IsStrictChildPath(this.edgeRoot, this.targetDirectory)
            || !ClientReleaseFileFacts.IsStrictChildPath(this.edgeRoot, privateDirectory)
            || !string.Equals(Path.GetDirectoryName(targetPackagePath), this.targetDirectory, StringComparison.Ordinal)
            || !string.Equals(Path.GetFileName(targetPackagePath), packageFileName, StringComparison.Ordinal))
        {
            throw new ClientReleaseValidationException("Edge 插件发布目录非法。");
        }
    }

    public string TargetPackagePath => targetPackagePath;

    public void Publish(string sourcePackagePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(privateDirectory)!);
        Directory.CreateDirectory(privateDirectory);
        privateDirectoryCreated = true;

        writeOwnershipMarker(privateOwnershipMarkerPath, ownershipToken);

        File.Move(sourcePackagePath, privatePackagePath, overwrite: false);
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
                || !IsCurrentOwnershipMarker(targetOwnershipMarkerPath))
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
               && IsCurrentOwnershipMarker(targetOwnershipMarkerPath)
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
            || !IsCurrentOwnershipMarker(privateOwnershipMarkerPath))
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
        return Directory.Exists(directory)
               && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0
               && ClientReleaseFileFacts.IsStrictChildPath(edgeRoot, directory)
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

    private bool IsCurrentOwnershipMarker(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            return false;
        }

        using var marker = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        if (marker.Length != 32)
        {
            return false;
        }

        Span<byte> actual = stackalloc byte[32];
        marker.ReadExactly(actual);
        Span<byte> expected = stackalloc byte[32];
        if (!Encoding.ASCII.TryGetBytes(ownershipToken, expected, out var bytesWritten)
            || bytesWritten != expected.Length)
        {
            return false;
        }

        return actual.SequenceEqual(expected);
    }

    private static void WriteOwnershipMarker(string path, string token)
    {
        var markerBytes = Encoding.ASCII.GetBytes(token);
        using var marker = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough);
        marker.Write(markerBytes);
        marker.Flush(flushToDisk: true);
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
