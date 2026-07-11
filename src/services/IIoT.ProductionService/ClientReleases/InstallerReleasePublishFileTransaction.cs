using IIoT.Services.Contracts;
using Microsoft.Extensions.Logging;

namespace IIoT.ProductionService.ClientReleases;

internal sealed class InstallerReleasePublishFileTransaction
{
    private const string OwnershipMarkerFileName = ".iiot-host-publish-owner";
    private readonly string edgeRoot;
    private readonly string targetDirectory;
    private readonly string targetMarkerPath;
    private readonly string privateDirectory;
    private readonly string privateMarkerPath;
    private readonly string ownershipToken = ClientReleaseOwnershipMarker.CreateToken();
    private readonly ILogger logger;
    private readonly Action<string, string> writeOwnershipMarker;
    private ClientReleaseDirectorySnapshot? snapshot;
    private bool privateDirectoryCreated;
    private bool targetOwned;
    private bool markerRemoved;

    public InstallerReleasePublishFileTransaction(
        string edgeRoot,
        string targetDirectory,
        ILogger logger,
        Action<string, string>? writeOwnershipMarker = null)
    {
        this.edgeRoot = Path.GetFullPath(edgeRoot);
        this.targetDirectory = Path.GetFullPath(targetDirectory);
        targetMarkerPath = Path.Combine(this.targetDirectory, OwnershipMarkerFileName);
        privateDirectory = Path.Combine(
            Path.GetDirectoryName(this.targetDirectory)!,
            $".host-publish-{Guid.NewGuid():N}");
        privateMarkerPath = Path.Combine(privateDirectory, OwnershipMarkerFileName);
        this.logger = logger;
        this.writeOwnershipMarker = writeOwnershipMarker ?? ClientReleaseOwnershipMarker.Write;

        ClientReleaseControlledDirectory.ValidateChain(
            this.edgeRoot,
            this.targetDirectory,
            "Edge installer 发布目录非法。",
            requireStrictChild: true);
        ClientReleaseControlledDirectory.ValidateChain(
            this.edgeRoot,
            privateDirectory,
            "Edge installer 发布目录非法。",
            requireStrictChild: true);
    }

    public string TargetDirectory => targetDirectory;

    public void Publish(string sourceDirectory)
    {
        var fullSource = Path.GetFullPath(sourceDirectory);
        ClientReleaseControlledDirectory.ValidateChain(
            edgeRoot,
            fullSource,
            "Edge installer 暂存目录非法。",
            requireStrictChild: true);

        snapshot = ClientReleaseDirectorySnapshot.Capture(fullSource);
        if (snapshot.Files.Any(file =>
                string.Equals(file.RelativePath, OwnershipMarkerFileName, StringComparison.Ordinal)))
        {
            throw new ClientReleaseValidationException("Edge installer 包含保留的发布所有权文件。");
        }

        ClientReleaseControlledDirectory.EnsureExists(
            edgeRoot,
            Path.GetDirectoryName(targetDirectory)!,
            createdDirectories: null,
            "Edge installer 发布目录非法。");
        writeOwnershipMarker(
            Path.Combine(fullSource, OwnershipMarkerFileName),
            ownershipToken);
        Directory.Move(fullSource, privateDirectory);
        privateDirectoryCreated = true;
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

    public bool HasExactPublishedDirectory()
    {
        try
        {
            return targetOwned
                   && ClientReleaseControlledDirectory.IsExistingDirectory(edgeRoot, targetDirectory)
                   && snapshot is not null
                   && (markerRemoved
                       ? snapshot.Matches(targetDirectory)
                       : ClientReleaseOwnershipMarker.Matches(targetMarkerPath, ownershipToken)
                         && snapshot.Matches(targetDirectory, OwnershipMarkerFileName));
        }
        catch (Exception ex)
        {
            ClientReleasePublishDiagnostics.LogFailure(
                logger,
                LogLevel.Warning,
                ClientReleasePublishDiagnostics.HostCommitUnknown,
                "host-installer-static-observation",
                ex,
                "host-installer");
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
            if (!ClientReleaseControlledDirectory.IsExistingDirectory(edgeRoot, targetDirectory)
                || snapshot is null
                || !ClientReleaseOwnershipMarker.Matches(targetMarkerPath, ownershipToken)
                || !snapshot.Matches(targetDirectory, OwnershipMarkerFileName))
            {
                ClientReleasePublishDiagnostics.LogCondition(
                    logger,
                    ClientReleasePublishDiagnostics.HostRollbackOwnershipMismatch,
                    "host-installer-rollback",
                    "owned-directory-validation-failed",
                    "host-installer");
                return false;
            }

            DeleteKnownOwnedDirectory(targetDirectory, targetMarkerPath, snapshot);
            targetOwned = false;
            return true;
        }
        catch (Exception ex)
        {
            ClientReleasePublishDiagnostics.LogFailure(
                logger,
                LogLevel.Warning,
                ClientReleasePublishDiagnostics.HostRollbackCleanupFailed,
                "host-installer-rollback",
                ex,
                "host-installer");
            return false;
        }
    }

    public bool TryRemoveOwnershipMarker()
    {
        if (markerRemoved)
        {
            return true;
        }

        try
        {
            if (!targetOwned
                || !ClientReleaseControlledDirectory.IsExistingDirectory(edgeRoot, targetDirectory)
                || !ClientReleaseOwnershipMarker.Matches(targetMarkerPath, ownershipToken))
            {
                return false;
            }

            File.Delete(targetMarkerPath);
            markerRemoved = true;
            return snapshot?.Matches(targetDirectory) == true;
        }
        catch (Exception ex)
        {
            ClientReleasePublishDiagnostics.LogFailure(
                logger,
                LogLevel.Warning,
                ClientReleasePublishDiagnostics.HostOwnershipMarkerCleanupFailed,
                "host-installer-owner-marker-cleanup",
                ex,
                "host-installer");
            return false;
        }
    }

    private bool TryDeletePrivateDirectory()
    {
        if (!privateDirectoryCreated || !Directory.Exists(privateDirectory))
        {
            return true;
        }

        try
        {
            if (!ClientReleaseControlledDirectory.IsExistingDirectory(edgeRoot, privateDirectory)
                || snapshot is null
                || !ClientReleaseOwnershipMarker.Matches(privateMarkerPath, ownershipToken)
                || !snapshot.Matches(privateDirectory, OwnershipMarkerFileName))
            {
                return false;
            }

            DeleteKnownOwnedDirectory(privateDirectory, privateMarkerPath, snapshot);
            privateDirectoryCreated = false;
            return true;
        }
        catch (Exception ex)
        {
            ClientReleasePublishDiagnostics.LogFailure(
                logger,
                LogLevel.Warning,
                ClientReleasePublishDiagnostics.HostRollbackCleanupFailed,
                "host-installer-private-cleanup",
                ex,
                "host-installer");
            return false;
        }
    }

    private static void DeleteKnownOwnedDirectory(
        string directory,
        string markerPath,
        ClientReleaseDirectorySnapshot snapshot)
    {
        foreach (var file in snapshot.Files)
        {
            var path = Path.Combine(directory, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!ClientReleaseFileFacts.IsExactRegularFile(path, file.Sha256, file.Size))
            {
                throw new InvalidOperationException("Owned installer file changed before rollback.");
            }

            File.Delete(path);
        }

        File.Delete(markerPath);
        foreach (var relativeDirectory in snapshot.Directories
                     .OrderByDescending(path => path.Count(ch => ch == '/'))
                     .ThenByDescending(path => path, StringComparer.Ordinal))
        {
            Directory.Delete(
                Path.Combine(directory, relativeDirectory.Replace('/', Path.DirectorySeparatorChar)),
                recursive: false);
        }

        Directory.Delete(directory, recursive: false);
    }
}
