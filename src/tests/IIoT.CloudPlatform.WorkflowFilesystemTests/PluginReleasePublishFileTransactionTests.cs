using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIoT.CloudPlatform.WorkflowTests;

public sealed class PluginReleasePublishFileTransactionTests
{
    [Fact]
    public void Publish_WhenTargetIsOccupiedAtAtomicMove_DoesNotMutateOrDeleteCompetingDirectory()
    {
        using var fixture = PublishFileFixture.Create("atomic-move-conflict");
        var transaction = fixture.CreateTransaction();
        Directory.CreateDirectory(fixture.TargetDirectory);
        var competingFile = Path.Combine(fixture.TargetDirectory, "competitor.txt");
        File.WriteAllText(competingFile, "competitor-owned");

        Assert.Throws<ClientReleasePublishConflictException>(() => transaction.Publish(fixture.SourcePackage));

        Assert.Equal("competitor-owned", File.ReadAllText(competingFile));
        Assert.False(File.Exists(Path.Combine(fixture.TargetDirectory, fixture.PackageFileName)));
        Assert.False(File.Exists(Path.Combine(fixture.TargetDirectory, ".iiot-plugin-publish-owner")));
        Assert.Empty(Directory.EnumerateDirectories(
            Path.GetDirectoryName(fixture.TargetDirectory)!,
            ".plugin-publish-*",
            SearchOption.TopDirectoryOnly));
        Assert.True(transaction.TryRollbackBeforeSave());
        Assert.True(Directory.Exists(fixture.TargetDirectory));
    }

    [Fact]
    public void TryRollbackBeforeSave_WhenOwnedTargetIsExact_RemovesOnlyOwnedDirectory()
    {
        using var fixture = PublishFileFixture.Create("exact-rollback");
        var transaction = fixture.CreateTransaction();
        transaction.Publish(fixture.SourcePackage);

        Assert.True(transaction.TryRollbackBeforeSave());

        Assert.False(Directory.Exists(fixture.TargetDirectory));
    }

    [Fact]
    public void TryRollbackBeforeSave_WhenOwnedTargetHasExtraEntry_FailsClosedAndPreservesDirectory()
    {
        using var fixture = PublishFileFixture.Create("extra-entry");
        var transaction = fixture.CreateTransaction();
        transaction.Publish(fixture.SourcePackage);
        var extraFile = Path.Combine(fixture.TargetDirectory, "unexpected.txt");
        File.WriteAllText(extraFile, "do-not-delete");

        Assert.False(transaction.TryRollbackBeforeSave());

        Assert.True(File.Exists(extraFile));
        Assert.True(File.Exists(transaction.TargetPackagePath));
    }

    [Fact]
    public void TryRollbackBeforeSave_WhenPackageHashChanges_FailsClosedAndPreservesDirectory()
    {
        using var fixture = PublishFileFixture.Create("hash-mismatch");
        var transaction = fixture.CreateTransaction();
        transaction.Publish(fixture.SourcePackage);
        File.AppendAllText(transaction.TargetPackagePath, "tampered");

        Assert.False(transaction.TryRollbackBeforeSave());

        Assert.True(Directory.Exists(fixture.TargetDirectory));
        Assert.True(File.Exists(transaction.TargetPackagePath));
    }

    [Fact]
    public void TryRollbackBeforeSave_WhenOwnershipMarkerIsReplacedByLargeFile_FailsClosedAndPreservesDirectory()
    {
        using var fixture = PublishFileFixture.Create("marker-replaced");
        var transaction = fixture.CreateTransaction();
        transaction.Publish(fixture.SourcePackage);
        var markerPath = Path.Combine(fixture.TargetDirectory, ".iiot-plugin-publish-owner");
        File.WriteAllBytes(markerPath, new byte[1024 * 1024]);

        Assert.False(transaction.TryRollbackBeforeSave());

        Assert.True(Directory.Exists(fixture.TargetDirectory));
        Assert.True(File.Exists(markerPath));
        Assert.True(File.Exists(transaction.TargetPackagePath));
    }

    [Fact]
    public void TryRemoveOwnershipMarker_AfterSuccessfulRemoval_IsIdempotent()
    {
        using var fixture = PublishFileFixture.Create("marker-idempotent");
        var transaction = fixture.CreateTransaction();
        transaction.Publish(fixture.SourcePackage);

        Assert.True(transaction.TryRemoveOwnershipMarker());
        Assert.True(transaction.TryRemoveOwnershipMarker());
        Assert.True(transaction.HasExactPublishedPackage());
        Assert.False(File.Exists(Path.Combine(fixture.TargetDirectory, ".iiot-plugin-publish-owner")));
        Assert.True(File.Exists(transaction.TargetPackagePath));
    }

    [Fact]
    public void TryRollbackBeforeSave_WhenMarkerWriteFailsBeforeCreation_RemovesExactEmptyPrivateDirectory()
    {
        using var fixture = PublishFileFixture.Create("marker-create-failure");
        var transaction = fixture.CreateTransaction(
            (_, _) => throw new IOException("marker-create-failed"));

        Assert.Throws<IOException>(() => transaction.Publish(fixture.SourcePackage));
        Assert.True(transaction.TryRollbackBeforeSave());

        var targetParent = Path.GetDirectoryName(fixture.TargetDirectory)!;
        Assert.Empty(Directory.EnumerateDirectories(
            targetParent,
            ".plugin-publish-*",
            SearchOption.TopDirectoryOnly));
        Assert.False(Directory.Exists(fixture.TargetDirectory));
        Assert.True(File.Exists(fixture.SourcePackage));
    }

    [Fact]
    public void Publish_WhenTargetTraversesSymlinkAncestor_ShouldRejectWithoutWritingOutsideRoot()
    {
        if (!ExternalDirectorySymlink.IsSupported)
        {
            return;
        }

        using var fixture = PublishFileFixture.Create("symlink-target");
        using var symlink = ExternalDirectorySymlink.Create(
            Path.Combine(fixture.Root, "plugins"),
            "plugin-target");

        Assert.Throws<ClientReleaseValidationException>(() => fixture.CreateTransaction());

        Assert.Empty(Directory.EnumerateFileSystemEntries(symlink.OutsideRoot));
        Assert.True(File.Exists(fixture.SourcePackage));
    }

    private sealed class PublishFileFixture(
        string root,
        string targetDirectory,
        string sourcePackage,
        string packageFileName,
        string sha256,
        long size)
        : IDisposable
    {
        public string Root { get; } = root;

        public string TargetDirectory { get; } = targetDirectory;

        public string SourcePackage { get; } = sourcePackage;

        public string PackageFileName { get; } = packageFileName;

        public static PublishFileFixture Create(string label)
        {
            var root = Path.Combine(Path.GetTempPath(), $"iiot-plugin-file-{label}-{Guid.NewGuid():N}");
            var incoming = Path.Combine(root, ".staging", "incoming");
            Directory.CreateDirectory(incoming);
            const string packageFileName = "plugin.zip";
            var sourcePackage = Path.Combine(incoming, packageFileName);
            File.WriteAllText(sourcePackage, "owned-plugin-package");
            var targetDirectory = Path.Combine(root, "plugins", "stable", "Module", "1.0.0");
            return new PublishFileFixture(
                root,
                targetDirectory,
                sourcePackage,
                packageFileName,
                ClientReleaseFileFacts.ComputeSha256(sourcePackage),
                new FileInfo(sourcePackage).Length);
        }

        public PluginReleasePublishFileTransaction CreateTransaction(
            Action<string, string>? writeOwnershipMarker = null)
        {
            return new PluginReleasePublishFileTransaction(
                Root,
                TargetDirectory,
                PackageFileName,
                sha256,
                size,
                NullLogger.Instance,
                writeOwnershipMarker);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
