using System.Data.Common;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.ClientReleases;
using IIoT.ServiceLayer.Tests.TestInfrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace IIoT.ServiceLayer.Tests;

public sealed class ClientReleaseVersionObservationReaderTests
{
    [Fact]
    public async Task ObserveAsync_ShouldUseNewContextAndOneSqlCommandPerObservation()
    {
        var interceptor = new ObservationCommandInterceptor();
        await using var database = await SqliteEfTestDatabase.CreateAsync(interceptor);
        var identity = await SeedPluginReleaseAsync(database, new string('a', 64));
        var reader = new EfClientReleaseVersionObservationReader(database.Options);

        var first = await reader.ObserveAsync(identity, CancellationToken.None);
        var second = await reader.ObserveAsync(identity, CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(2, interceptor.ObservationCommandCount);
        Assert.Equal(2, interceptor.ObservationContexts.Count);
        Assert.NotSame(interceptor.ObservationContexts[0], interceptor.ObservationContexts[1]);
        Assert.All(
            interceptor.ObservationCommands,
            command => Assert.Contains("edge_client_release_artifacts", command, StringComparison.Ordinal));
        Assert.Equal(2, first!.Artifacts.Count);
        Assert.Equal(new string('a', 64), first.Sha256);
    }

    [Fact]
    public async Task ObserveAsync_WhenStateMutatesAtCommandBoundary_ShouldNotAssembleSplitQueryFalseExact()
    {
        var interceptor = new ObservationCommandInterceptor(pauseFirstObservation: true);
        await using var database = await SqliteEfTestDatabase.CreateAsync(interceptor);
        var initialArtifactHash = new string('b', 64);
        var finalArtifactHash = new string('a', 64);
        const string mutatedReleaseNotes = "mutated-state";
        var identity = await SeedPluginReleaseAsync(database, initialArtifactHash);
        var reader = new EfClientReleaseVersionObservationReader(database.Options);

        var observationTask = reader.ObserveAsync(identity, CancellationToken.None);
        await interceptor.QueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await using var mutationContext = database.CreateContext();
            await mutationContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE edge_client_release_versions SET release_notes = {mutatedReleaseNotes} WHERE version = {identity.Version}");
            await mutationContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE edge_client_release_artifacts SET sha256 = {finalArtifactHash} WHERE artifact_kind = {nameof(ClientReleaseArtifactKind.PackageFile)}");
        }
        finally
        {
            interceptor.ResumeQuery();
        }

        var observation = await observationTask;

        Assert.NotNull(observation);
        Assert.Equal(1, interceptor.ObservationCommandCount);
        Assert.Equal(mutatedReleaseNotes, observation!.ReleaseNotes);
        var package = Assert.Single(
            observation.Artifacts,
            artifact => artifact.ArtifactKind == ClientReleaseArtifactKind.PackageFile);
        Assert.Equal(finalArtifactHash, package.Sha256);
    }

    private static async Task<ClientReleaseVersionIdentity> SeedPluginReleaseAsync(
        SqliteEfTestDatabase database,
        string packageArtifactHash)
    {
        const string moduleId = "ObservationModule";
        const string channel = "stable";
        const string targetRuntime = "win-x64";
        const string version = "1.0.0";
        var component = ClientReleaseComponent.CreatePlugin(
            moduleId,
            "Observation module",
            "observation test",
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
            "/edge-updates/plugins/stable/ObservationModule/1.0.0/plugin.zip",
            new string('a', 64),
            128,
            "expected-state",
            "[]",
            ClientReleaseStatus.Published,
            null,
            "IIoT",
            new DateTime(2026, 7, 11, 3, 0, 0, DateTimeKind.Utc),
            [
                new ClientReleaseArtifact(
                    ClientReleaseArtifactKind.PluginPackageDirectory,
                    "plugins/stable/ObservationModule/1.0.0"),
                new ClientReleaseArtifact(
                    ClientReleaseArtifactKind.PackageFile,
                    "plugins/stable/ObservationModule/1.0.0/plugin.zip",
                    packageArtifactHash,
                    128)
            ]);
        await using var context = database.CreateContext();
        context.ClientReleaseComponents.Add(component);
        await context.SaveChangesAsync();
        return new ClientReleaseVersionIdentity(
            ClientReleaseComponentKind.Plugin,
            moduleId,
            channel,
            targetRuntime,
            version);
    }

    private sealed class ObservationCommandInterceptor(bool pauseFirstObservation = false)
        : DbCommandInterceptor
    {
        private readonly TaskCompletionSource<bool> queryStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> resume = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int pauseClaimed;

        public int ObservationCommandCount { get; private set; }

        public List<DbContext> ObservationContexts { get; } = [];

        public List<string> ObservationCommands { get; } = [];

        public TaskCompletionSource<bool> QueryStarted => queryStarted;

        public void ResumeQuery() => resume.TrySetResult(true);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (!IsObservationCommand(command.CommandText))
            {
                return result;
            }

            ObservationCommandCount++;
            ObservationContexts.Add(Assert.IsType<IIoTDbContext>(eventData.Context));
            ObservationCommands.Add(command.CommandText);
            if (pauseFirstObservation && Interlocked.CompareExchange(ref pauseClaimed, 1, 0) == 0)
            {
                queryStarted.TrySetResult(true);
                await resume.Task.WaitAsync(cancellationToken);
            }

            return result;
        }

        private static bool IsObservationCommand(string commandText)
            => commandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
               && commandText.Contains("edge_client_release_components", StringComparison.Ordinal);
    }
}
