using System.Data.Common;
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

namespace IIoT.CloudPlatform.Persistence.PostgresTests;

[Collection(PostgresPersistenceIntegrationCollection.Name)]
public sealed class ClientReleaseCommitRecoveryPostgresTests(
    ClientReleaseCommitRecoveryPostgresFixture fixture)
{
    [Fact]
    public async Task PostCommitExceptionSimulation_ShouldBeConfirmedByFreshPostgresObservation()
    {
        const string sentinel = "post-commit-exception-simulation";
        var connectionString = await fixture.GetConnectionStringAsync();
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
            var expected = new ClientReleaseExpectedVersionState(
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
            Assert.Single(await reader.ObserveAsync([expected.Identity], CancellationToken.None));
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

    [Fact]
    public async Task HostAndGeneratedPluginPostCommitExceptionSimulation_ShouldUseOneFreshPostgresBatchObservation()
    {
        const string sentinel = "host-post-commit-exception-simulation";
        var connectionString = await fixture.GetConnectionStringAsync();
        var observationInterceptor = new ObservationCommandInterceptor();
        var cleanOptions = new DbContextOptionsBuilder<IIoTDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(observationInterceptor)
            .Options;
        var saveInterceptor = new ThrowAfterCommittedSaveInterceptor(sentinel);
        var saveOptions = new DbContextOptionsBuilder<IIoTDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(saveInterceptor)
            .Options;
        var unique = Guid.NewGuid().ToString("N");
        var channel = $"recovery-{unique[..8]}";
        var moduleId = $"RecoveryHost{unique[..12]}";
        const string targetRuntime = "win-x64";
        const string hostVersionText = "2.0.0";
        const string pluginVersionText = "1.0.0";
        var publishedAtUtc = new DateTime(2026, 7, 11, 5, 0, 0, DateTimeKind.Utc).AddTicks(9);
        var edgeRoot = Path.Combine(Path.GetTempPath(), $"iiot-pg-host-recovery-{unique}");
        try
        {
            Directory.CreateDirectory(edgeRoot);
            var installerSource = Path.Combine(edgeRoot, ".staging", "installer-source");
            WriteText(Path.Combine(installerSource, "installer-artifact.json"), "host manifest");
            WriteText(Path.Combine(installerSource, "IIoT.Edge.Setup.exe"), "host setup");
            var installerTarget = Path.Combine(edgeRoot, "installers", channel, hostVersionText);
            var installerTransaction = new InstallerReleasePublishFileTransaction(
                edgeRoot,
                installerTarget,
                NullLogger.Instance);
            installerTransaction.Publish(installerSource);

            var velopackSource = Path.Combine(edgeRoot, ".staging", "velopack-source");
            WriteText(Path.Combine(velopackSource, $"IIoT.EdgeClient-{hostVersionText}-full.nupkg"), "host nupkg");
            WriteText(Path.Combine(velopackSource, "RELEASES-stable"), "host releases");
            WriteText(Path.Combine(velopackSource, "releases.stable.json"), "host releases json");
            WriteText(Path.Combine(velopackSource, "assets.stable.json"), "host assets json");
            var velopackTarget = Path.Combine(edgeRoot, "velopack", channel);
            var velopackTransaction = new VelopackReleasePublishFileTransaction(
                edgeRoot,
                velopackSource,
                velopackTarget,
                Path.Combine(edgeRoot, ".staging", "velopack-backup"),
                NullLogger.Instance);
            velopackTransaction.Publish();

            var pluginBytes = Encoding.UTF8.GetBytes("generated plugin package");
            var pluginSha256 = Convert.ToHexString(SHA256.HashData(pluginBytes)).ToLowerInvariant();
            var pluginSource = Path.Combine(edgeRoot, ".staging", "plugin.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(pluginSource)!);
            File.WriteAllBytes(pluginSource, pluginBytes);
            var pluginRelativeDirectory = $"plugins/{channel}/{moduleId}/{pluginVersionText}";
            var pluginTarget = Path.Combine(
                edgeRoot,
                pluginRelativeDirectory.Replace('/', Path.DirectorySeparatorChar));
            var pluginTransaction = new PluginReleasePublishFileTransaction(
                edgeRoot,
                pluginTarget,
                "plugin.zip",
                pluginSha256,
                pluginBytes.LongLength,
                NullLogger.Instance);
            pluginTransaction.Publish(pluginSource);

            var manifestDownloadUrl = $"/edge-updates/installers/{channel}/{hostVersionText}/installer-artifact.json";
            var manifestFact = ClientReleaseFileFacts.GetFileFact(
                Path.Combine(installerTarget, "installer-artifact.json"));
            var installerFact = ClientReleaseFileFacts.GetFileFact(
                Path.Combine(installerTarget, "IIoT.Edge.Setup.exe"));
            var hostArtifacts = ClientReleaseArtifactBuilder.FromPublishedHostFiles(
                    manifestDownloadUrl,
                    channel,
                    hostVersionText,
                    manifestFact,
                    "IIoT.Edge.Setup.exe",
                    installerFact)
                .Concat(velopackTransaction.PublishedFiles.Select(file =>
                    ClientReleaseArtifactBuilder.VelopackFile(
                        channel,
                        file.RelativePath,
                        file.Sha256,
                        file.Size)))
                .ToArray();
            var hostComponent = ClientReleaseComponent.CreateHost(channel, targetRuntime);
            var hostVersion = hostComponent.UpsertHostVersion(
                hostVersionText,
                "1.0.0",
                "net10.0",
                manifestDownloadUrl,
                installerFact.Sha256,
                installerFact.Size,
                "Host PostgreSQL recovery verification",
                ClientReleaseStatus.Published,
                null,
                "IIoT",
                publishedAtUtc,
                hostArtifacts);

            var pluginDownloadUrl = $"/edge-updates/{pluginRelativeDirectory}/plugin.zip";
            var pluginComponent = ClientReleaseComponent.CreatePlugin(
                moduleId,
                "Generated recovery plugin",
                "Host bundle generated plugin",
                null,
                null,
                channel,
                targetRuntime);
            var pluginVersion = pluginComponent.UpsertPluginVersion(
                pluginVersionText,
                "1.0.0",
                "1.0.0",
                "99.0.0",
                "net10.0",
                pluginDownloadUrl,
                pluginSha256,
                pluginBytes.LongLength,
                "Host PostgreSQL recovery verification",
                "[]",
                ClientReleaseStatus.Published,
                null,
                "IIoT",
                publishedAtUtc,
                ClientReleaseArtifactBuilder.FromPluginDownloadUrl(
                    pluginDownloadUrl,
                    channel,
                    moduleId,
                    pluginVersionText,
                    pluginSha256,
                    pluginBytes.LongLength));
            var expected = new[]
            {
                ClientReleaseExpectedVersionState.From(hostComponent, hostVersion),
                ClientReleaseExpectedVersionState.From(pluginComponent, pluginVersion)
            };

            await using (var saveContext = new IIoTDbContext(saveOptions))
            {
                saveContext.ClientReleaseComponents.AddRange(hostComponent, pluginComponent);
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => saveContext.SaveChangesAsync());
                Assert.Equal(sentinel, exception.Message);
            }

            var reader = new EfClientReleaseVersionObservationReader(cleanOptions);
            var outcome = await HostReleaseCommitRecovery.ObserveAsync(
                reader,
                expected,
                installerTransaction,
                velopackTransaction,
                [pluginTransaction],
                NullLogger.Instance);

            Assert.Equal(HostReleaseCommitObservationOutcome.Committed, outcome);
            Assert.Equal(1, observationInterceptor.ObservationCommandCount);
            Assert.True(installerTransaction.TryRemoveOwnershipMarker());
            Assert.True(pluginTransaction.TryRemoveOwnershipMarker());
            Assert.True(velopackTransaction.HasExactPublishedFiles());
            Assert.Equal(1, saveInterceptor.PostCommitExceptionsThrown);
        }
        finally
        {
            if (Directory.Exists(edgeRoot))
            {
                Directory.Delete(edgeRoot, recursive: true);
            }
        }
    }

    private static void WriteText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
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

    private sealed class ObservationCommandInterceptor : DbCommandInterceptor
    {
        public int ObservationCommandCount { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("edge_client_release_components", StringComparison.Ordinal))
            {
                ObservationCommandCount++;
            }

            return ValueTask.FromResult(result);
        }
    }
}

[CollectionDefinition("Postgres persistence integration", DisableParallelization = true)]
public sealed class PostgresPersistenceIntegrationCollection
    : ICollectionFixture<ClientReleaseCommitRecoveryPostgresFixture>
{
    public const string Name = "Postgres persistence integration";
}

public sealed class ClientReleaseCommitRecoveryPostgresFixture : IAsyncLifetime
{
    private readonly IIoTAppFixture fixture = new(disableDataWorkerOutboxDispatcher: true);

    public Task InitializeAsync() => fixture.StartAsync();

    public Task DisposeAsync() => fixture.DisposeAsync().AsTask();

    public bool DataWorkerOutboxDispatcherDisabled => fixture.DataWorkerOutboxDispatcherDisabled;

    public Task<string> GetConnectionStringAsync()
        => fixture.GetConnectionStringAsync(ConnectionResourceNames.IiotDatabase);

    public Task<string> GetConnectionStringAsync(string resourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        return fixture.GetConnectionStringAsync(resourceName);
    }
}
