using System.Text;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIoT.CloudPlatform.WorkflowTests;

public sealed class HostReleasePublishFileTransactionTests
{
    [Fact]
    public void InstallerPublish_WhenTargetAppears_ShouldKeepExistingDirectoryAndRemovePrivateOwnedCopy()
    {
        using var fixture = new TransactionFixture();
        fixture.WriteSource("installer-artifact.json", "new-manifest");
        fixture.WriteTarget("existing.txt", "existing");
        var transaction = fixture.CreateInstallerTransaction();

        Assert.Throws<ClientReleasePublishConflictException>(() =>
            transaction.Publish(fixture.SourceRoot));

        Assert.Equal("existing", File.ReadAllText(Path.Combine(fixture.TargetRoot, "existing.txt")));
        Assert.DoesNotContain(
            Directory.EnumerateDirectories(Path.GetDirectoryName(fixture.TargetRoot)!),
            path => Path.GetFileName(path).StartsWith(".host-publish-", StringComparison.Ordinal));
    }

    [Fact]
    public void InstallerPublish_WhenOwnershipMarkerWriteFails_ShouldKeepAllFilesInsideUploadStaging()
    {
        using var fixture = new TransactionFixture();
        fixture.WriteSource("installer-artifact.json", "manifest");
        var transaction = new InstallerReleasePublishFileTransaction(
            fixture.EdgeRoot,
            fixture.TargetRoot,
            NullLogger.Instance,
            (path, token) =>
            {
                File.WriteAllText(path, token[..8], new UTF8Encoding(false));
                throw new IOException("marker-flush-failed");
            });

        Assert.Throws<IOException>(() => transaction.Publish(fixture.SourceRoot));

        Assert.True(Directory.Exists(fixture.SourceRoot));
        Assert.True(File.Exists(Path.Combine(fixture.SourceRoot, "installer-artifact.json")));
        var partialMarker = Path.Combine(fixture.SourceRoot, ".iiot-host-publish-owner");
        Assert.True(File.Exists(partialMarker));
        Assert.Equal(8, new FileInfo(partialMarker).Length);
        Assert.False(Directory.Exists(fixture.TargetRoot));
        var targetParent = Path.GetDirectoryName(fixture.TargetRoot)!;
        Assert.DoesNotContain(
            Directory.EnumerateDirectories(targetParent),
            path => Path.GetFileName(path).StartsWith(".host-publish-", StringComparison.Ordinal));
    }

    [Fact]
    public void InstallerPublish_WhenTargetTraversesSymlinkAncestor_ShouldRejectWithoutWritingOutsideRoot()
    {
        if (!ExternalDirectorySymlink.IsSupported)
        {
            return;
        }

        using var fixture = new TransactionFixture();
        fixture.WriteSource("installer-artifact.json", "manifest");
        using var symlink = ExternalDirectorySymlink.Create(
            Path.Combine(fixture.EdgeRoot, "installers"),
            "installer-target");

        Assert.Throws<ClientReleaseValidationException>(() => fixture.CreateInstallerTransaction());

        Assert.Empty(Directory.EnumerateFileSystemEntries(symlink.OutsideRoot));
        Assert.True(File.Exists(Path.Combine(fixture.SourceRoot, "installer-artifact.json")));
    }

    [Fact]
    public void DirectorySnapshot_WhenChildDirectoryIsSymlink_ShouldRejectBeforeTraversal()
    {
        if (!ExternalDirectorySymlink.IsSupported)
        {
            return;
        }

        using var fixture = new TransactionFixture();
        fixture.WriteSource("installer-artifact.json", "manifest");
        using var symlink = ExternalDirectorySymlink.Create(
            Path.Combine(fixture.SourceRoot, "linked"),
            "installer-source-child");
        File.WriteAllText(Path.Combine(symlink.OutsideRoot, "outside.txt"), "outside");

        Assert.Throws<InvalidDataException>(() =>
            ClientReleaseDirectorySnapshot.Capture(fixture.SourceRoot));

        Assert.Equal("outside", File.ReadAllText(Path.Combine(symlink.OutsideRoot, "outside.txt")));
        Assert.False(Directory.Exists(fixture.TargetRoot));
    }

    [Fact]
    public void InstallerRollback_WhenOwnedDirectoryHasExtraFile_ShouldRefuseWithoutDeletingPublishedFiles()
    {
        using var fixture = new TransactionFixture();
        fixture.WriteSource("installer-artifact.json", "manifest");
        fixture.WriteSource("host/host.dll", "host");
        var transaction = fixture.CreateInstallerTransaction();
        transaction.Publish(fixture.SourceRoot);
        File.WriteAllText(Path.Combine(fixture.TargetRoot, "unexpected.txt"), "external", Encoding.UTF8);

        Assert.False(transaction.TryRollbackBeforeSave());

        Assert.True(File.Exists(Path.Combine(fixture.TargetRoot, "installer-artifact.json")));
        Assert.True(File.Exists(Path.Combine(fixture.TargetRoot, "unexpected.txt")));
    }

    [Fact]
    public void InstallerRollback_WhenOwnedFileWasTampered_ShouldRefuseWithoutDeletingDirectory()
    {
        using var fixture = new TransactionFixture();
        fixture.WriteSource("installer-artifact.json", "manifest");
        var transaction = fixture.CreateInstallerTransaction();
        transaction.Publish(fixture.SourceRoot);
        File.AppendAllText(Path.Combine(fixture.TargetRoot, "installer-artifact.json"), "tampered");

        Assert.False(transaction.TryRollbackBeforeSave());

        Assert.True(Directory.Exists(fixture.TargetRoot));
        Assert.Contains("tampered", File.ReadAllText(Path.Combine(fixture.TargetRoot, "installer-artifact.json")));
    }

    [Fact]
    public void InstallerRollback_WhenDirectoryIsExactOwned_ShouldDeleteOnlyKnownTree()
    {
        using var fixture = new TransactionFixture();
        fixture.WriteSource("installer-artifact.json", "manifest");
        fixture.WriteSource("host/host.dll", "host");
        var transaction = fixture.CreateInstallerTransaction();
        transaction.Publish(fixture.SourceRoot);

        Assert.True(transaction.TryRollbackBeforeSave());

        Assert.False(Directory.Exists(fixture.TargetRoot));
    }

    [Fact]
    public void VelopackRollback_ShouldRestoreExactPreviousManifestsAndRemoveNewPackage()
    {
        using var fixture = new TransactionFixture();
        var source = fixture.CreateDirectory(".staging/velopack-source");
        var target = fixture.CreateDirectory("velopack/stable");
        fixture.Write(source, "releases.stable.json", "new-manifest");
        fixture.Write(source, "assets.stable.json", "new-assets");
        fixture.Write(source, "RELEASES-stable", "new-releases");
        fixture.Write(source, "IIoT.EdgeClient-2.0.0-full.nupkg", "new-package");
        fixture.Write(target, "releases.stable.json", "old-manifest");
        fixture.Write(target, "assets.stable.json", "old-assets");
        fixture.Write(target, "RELEASES", "old-releases");
        var transaction = new VelopackReleasePublishFileTransaction(
            fixture.EdgeRoot,
            source,
            target,
            Path.Combine(fixture.EdgeRoot, ".staging", "velopack-backup"),
            NullLogger.Instance);
        transaction.Publish();

        Assert.True(transaction.TryRollbackBeforeSave());

        Assert.Equal("old-manifest", File.ReadAllText(Path.Combine(target, "releases.stable.json")));
        Assert.Equal("old-assets", File.ReadAllText(Path.Combine(target, "assets.stable.json")));
        Assert.Equal("old-releases", File.ReadAllText(Path.Combine(target, "RELEASES")));
        Assert.False(File.Exists(Path.Combine(target, "RELEASES-stable")));
        Assert.False(File.Exists(Path.Combine(target, "IIoT.EdgeClient-2.0.0-full.nupkg")));
        Assert.DoesNotContain(
            Directory.EnumerateFileSystemEntries(target),
            path => Path.GetFileName(path).Contains(".publish-", StringComparison.Ordinal)
                    || Path.GetFileName(path).Contains(".restore-", StringComparison.Ordinal));
    }

    [Fact]
    public void VelopackPublish_WhenExistingPackageIsExact_ShouldReuseAndNeverOwnItForRollback()
    {
        using var fixture = new TransactionFixture();
        var source = fixture.CreateDirectory(".staging/velopack-source");
        var target = fixture.CreateDirectory("velopack/stable");
        fixture.Write(source, "IIoT.EdgeClient-2.0.0-full.nupkg", "same-package");
        fixture.Write(target, "IIoT.EdgeClient-2.0.0-full.nupkg", "same-package");
        var transaction = new VelopackReleasePublishFileTransaction(
            fixture.EdgeRoot,
            source,
            target,
            Path.Combine(fixture.EdgeRoot, ".staging", "velopack-backup"),
            NullLogger.Instance);
        transaction.Publish();

        Assert.True(transaction.TryRollbackBeforeSave());

        Assert.Equal("same-package", File.ReadAllText(Path.Combine(target, "IIoT.EdgeClient-2.0.0-full.nupkg")));
    }

    [Fact]
    public void VelopackPublish_WhenExistingPackageDiffers_ShouldConflictWithoutOverwrite()
    {
        using var fixture = new TransactionFixture();
        var source = fixture.CreateDirectory(".staging/velopack-source");
        var target = fixture.CreateDirectory("velopack/stable");
        fixture.Write(source, "IIoT.EdgeClient-2.0.0-full.nupkg", "new-package");
        fixture.Write(target, "IIoT.EdgeClient-2.0.0-full.nupkg", "existing-package");
        var transaction = new VelopackReleasePublishFileTransaction(
            fixture.EdgeRoot,
            source,
            target,
            Path.Combine(fixture.EdgeRoot, ".staging", "velopack-backup"),
            NullLogger.Instance);

        Assert.Throws<ClientReleasePublishConflictException>(() => transaction.Publish());

        Assert.Equal("existing-package", File.ReadAllText(Path.Combine(target, "IIoT.EdgeClient-2.0.0-full.nupkg")));
    }

    [Fact]
    public void VelopackPublish_WhenMissingTargetTraversesReparseAncestor_ShouldRejectWithoutWritingOutsideRoot()
    {
        if (!ExternalDirectorySymlink.IsSupported)
        {
            return;
        }

        using var fixture = new TransactionFixture();
        var source = fixture.CreateDirectory(".staging/velopack-source");
        fixture.Write(source, "releases.stable.json", "manifest");
        var linkParent = fixture.CreateDirectory("velopack");
        using var symlink = ExternalDirectorySymlink.Create(
            Path.Combine(linkParent, "linked"),
            "velopack-target");
        var target = Path.Combine(symlink.LinkPath, "stable");
        Assert.Throws<ClientReleaseValidationException>(() =>
        {
            var transaction = new VelopackReleasePublishFileTransaction(
                fixture.EdgeRoot,
                source,
                target,
                Path.Combine(fixture.EdgeRoot, ".staging", "velopack-backup"),
                NullLogger.Instance);
            transaction.Publish();
        });

        Assert.False(Directory.Exists(Path.Combine(symlink.OutsideRoot, "stable")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(symlink.OutsideRoot));
    }

    [Fact]
    public void VelopackRollback_WhenPublishedFileWasTampered_ShouldRefuseWithoutDeletingIt()
    {
        using var fixture = new TransactionFixture();
        var source = fixture.CreateDirectory(".staging/velopack-source");
        var target = fixture.CreateDirectory("velopack/stable");
        fixture.Write(source, "releases.stable.json", "new-manifest");
        var transaction = new VelopackReleasePublishFileTransaction(
            fixture.EdgeRoot,
            source,
            target,
            Path.Combine(fixture.EdgeRoot, ".staging", "velopack-backup"),
            NullLogger.Instance);
        transaction.Publish();
        File.AppendAllText(Path.Combine(target, "releases.stable.json"), "tampered");

        Assert.False(transaction.TryRollbackBeforeSave());

        Assert.Contains("tampered", File.ReadAllText(Path.Combine(target, "releases.stable.json")));
    }

    private sealed class TransactionFixture : IDisposable
    {
        public TransactionFixture()
        {
            EdgeRoot = Path.Combine(Path.GetTempPath(), $"iiot-host-file-transaction-{Guid.NewGuid():N}");
            SourceRoot = CreateDirectory(".staging/installer-source");
            TargetRoot = Path.Combine(EdgeRoot, "installers", "stable", "2.0.0");
        }

        public string EdgeRoot { get; }

        public string SourceRoot { get; }

        public string TargetRoot { get; }

        public InstallerReleasePublishFileTransaction CreateInstallerTransaction()
            => new(EdgeRoot, TargetRoot, NullLogger.Instance);

        public string CreateDirectory(string relativePath)
        {
            var path = Path.Combine(EdgeRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(path);
            return path;
        }

        public void WriteSource(string relativePath, string content)
            => Write(SourceRoot, relativePath, content);

        public void WriteTarget(string relativePath, string content)
        {
            Directory.CreateDirectory(TargetRoot);
            Write(TargetRoot, relativePath, content);
        }

        public void Write(string root, string relativePath, string content)
        {
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        public void Dispose()
        {
            if (Directory.Exists(EdgeRoot))
            {
                Directory.Delete(EdgeRoot, recursive: true);
            }
        }
    }
}
