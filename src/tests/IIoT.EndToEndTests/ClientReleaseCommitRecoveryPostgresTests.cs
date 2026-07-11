using System.Security.Cryptography;
using System.Text;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.SharedKernel.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIoT.EndToEndTests;

public sealed class ClientReleaseCommitRecoveryPostgresTests : IAsyncLifetime
{
    private readonly IIoTAppFixture fixture = new();

    public Task InitializeAsync() => fixture.StartAsync();

    public Task DisposeAsync() => fixture.DisposeAsync().AsTask();

    [Fact]
    public async Task PostCommitExceptionSimulation_ShouldBeConfirmedByFreshPostgresObservation()
    {
        const string sentinel = "post-commit-exception-simulation";
        var connectionString = await fixture.GetConnectionStringAsync(ConnectionResourceNames.IiotDatabase);
        var cleanOptions = new DbContextOptionsBuilder<IIoTDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var interceptor = new ThrowAfterCommittedSaveInterceptor(sentinel);
        var saveOptions = new DbContextOptionsBuilder<IIoTDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(interceptor)
            .Options;
        var unique = Guid.NewGuid().ToString("N");
        var moduleId = $"Recovery{unique}";
        const string channel = "stable";
        const string targetRuntime = "win-x64";
        const string version = "1.0.0";
        var downloadUrl = $"/edge-updates/plugins/{channel}/{moduleId}/{version}/plugin.zip";
        var packageBytes = Encoding.UTF8.GetBytes("postgres committed package");
        var sha256 = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        var packageSize = packageBytes.LongLength;
        var publishedAtUtc = new DateTime(2026, 7, 11, 4, 0, 0, DateTimeKind.Utc).AddTicks(7);
        var relativeDirectory = $"plugins/{channel}/{moduleId}/{version}";
        var relativePackage = $"{relativeDirectory}/plugin.zip";
        var component = ClientReleaseComponent.CreatePlugin(
            moduleId,
            "Recovery module",
            "Post-commit exception simulation",
            "Puzzle",
            "#abcdef",
            channel,
            targetRuntime);
        component.UpsertPluginVersion(
            version,
            "1.0.0",
            "1.0.0",
            "99.0.0",
            "net10.0",
            downloadUrl,
            sha256,
            packageSize,
            "PostgreSQL recovery verification",
            "[]",
            ClientReleaseStatus.Published,
            null,
            "IIoT",
            publishedAtUtc,
            [
                new ClientReleaseArtifact(
                    ClientReleaseArtifactKind.PluginPackageDirectory,
                    relativeDirectory),
                new ClientReleaseArtifact(
                    ClientReleaseArtifactKind.PackageFile,
                    relativePackage,
                    sha256,
                    packageSize)
            ]);

        await using (var saveContext = new IIoTDbContext(saveOptions))
        {
            saveContext.ClientReleaseComponents.Add(component);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => saveContext.SaveChangesAsync());
            Assert.Equal(sentinel, exception.Message);
        }

        var edgeRoot = Path.Combine(Path.GetTempPath(), $"iiot-pg-recovery-{unique}");
        try
        {
            Directory.CreateDirectory(edgeRoot);
            var sourcePackage = Path.Combine(edgeRoot, ".staging", "plugin.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePackage)!);
            File.WriteAllBytes(sourcePackage, packageBytes);
            var targetDirectory = Path.Combine(edgeRoot, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
            var fileTransaction = new PluginReleasePublishFileTransaction(
                edgeRoot,
                targetDirectory,
                "plugin.zip",
                sha256,
                packageSize,
                NullLogger.Instance);
            fileTransaction.Publish(sourcePackage);
            var expected = new PluginReleaseExpectedState(
                new ClientReleaseVersionIdentity(
                    ClientReleaseComponentKind.Plugin,
                    moduleId,
                    channel,
                    targetRuntime,
                    version),
                "Recovery module",
                "Post-commit exception simulation",
                "Puzzle",
                "#abcdef",
                "1.0.0",
                "1.0.0",
                "99.0.0",
                "net10.0",
                downloadUrl,
                sha256,
                packageSize,
                "PostgreSQL recovery verification",
                "[]",
                null,
                "IIoT",
                publishedAtUtc,
                [
                    new ClientReleaseArtifactObservation(
                        ClientReleaseArtifactKind.PluginPackageDirectory,
                        relativeDirectory,
                        null,
                        null),
                    new ClientReleaseArtifactObservation(
                        ClientReleaseArtifactKind.PackageFile,
                        relativePackage,
                        sha256,
                        packageSize)
                ]);
            var reader = new EfClientReleaseVersionObservationReader(cleanOptions);

            var outcome = await PluginReleaseCommitRecovery.ObserveAsync(
                reader,
                expected,
                fileTransaction,
                NullLogger.Instance);

            Assert.Equal(PluginReleaseCommitObservationOutcome.Committed, outcome);
            Assert.True(fileTransaction.TryRemoveOwnershipMarker());
            Assert.NotNull(await reader.ObserveAsync(expected.Identity, CancellationToken.None));
            Assert.Equal(1, interceptor.PostCommitExceptionsThrown);
        }
        finally
        {
            if (Directory.Exists(edgeRoot))
            {
                Directory.Delete(edgeRoot, recursive: true);
            }
        }
    }

    private sealed class ThrowAfterCommittedSaveInterceptor(string message) : SaveChangesInterceptor
    {
        public int PostCommitExceptionsThrown { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            PostCommitExceptionsThrown++;
            throw new InvalidOperationException(message);
        }
    }
}
