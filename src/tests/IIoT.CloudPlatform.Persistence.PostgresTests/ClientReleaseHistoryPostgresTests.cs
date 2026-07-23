using System.Data.Common;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.QueryServices;
using IIoT.Services.Contracts.RecordQueries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace IIoT.CloudPlatform.Persistence.PostgresTests;

[Collection(PostgresPersistenceIntegrationCollection.Name)]
public sealed class ClientReleaseHistoryPostgresTests(
    ClientReleaseCommitRecoveryPostgresFixture fixture)
{
    [Fact]
    public async Task EfHistoryQuery_ShouldFilterCountAndPageInPostgres_WithStableTieBreaker()
    {
        var connectionString = await fixture.GetConnectionStringAsync();
        var interceptor = new RecordingCommandInterceptor();
        var options = new DbContextOptionsBuilder<IIoTDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(interceptor)
            .Options;
        var channel = $"hist-{Guid.NewGuid():N}"[..18];
        var tieAtUtc = new DateTime(2026, 7, 23, 0, 0, 0, DateTimeKind.Utc);

        var activeOnly = CreatePluginComponent("ActiveOnly", channel, "win-x64", ClientReleaseStatus.Published);
        var archived = CreatePluginComponent("Archived", channel, "win-x64", ClientReleaseStatus.Published);
        SingleVersion(archived).ChangeStatus(ClientReleaseStatus.Archived);
        var deleted = CreatePluginComponent("Deleted", channel, "win-x64", ClientReleaseStatus.Published);
        SingleVersion(deleted).MarkDeleted("管理员删除");
        var failed = CreatePluginComponent("DeleteFailed", channel, "win-x64", ClientReleaseStatus.Published);
        SingleVersion(failed).MarkDeleteFailed("FileDeletionFailed");
        var otherRuntime = CreatePluginComponent("OtherRuntime", channel, "linux-x64", ClientReleaseStatus.Published);
        SingleVersion(otherRuntime).ChangeStatus(ClientReleaseStatus.Archived);
        var otherChannel = CreatePluginComponent(
            "OtherChannel",
            $"other-{Guid.NewGuid():N}"[..18],
            "win-x64",
            ClientReleaseStatus.Published);
        SingleVersion(otherChannel).ChangeStatus(ClientReleaseStatus.Archived);

        await using (var seed = new IIoTDbContext(options))
        {
            seed.ClientReleaseComponents.AddRange(
                activeOnly,
                archived,
                deleted,
                failed,
                otherRuntime,
                otherChannel);
            await seed.SaveChangesAsync();

            await SetHistoryTimestampAsync(seed, SingleVersion(archived), tieAtUtc, deleted: false);
            await SetHistoryTimestampAsync(seed, SingleVersion(deleted), tieAtUtc, deleted: true);
            await SetHistoryTimestampAsync(seed, SingleVersion(failed), tieAtUtc, deleted: false);
        }

        interceptor.CommandTexts.Clear();
        var firstPass = await ReadAllPagesAsync(options, channel);
        var secondPass = await ReadAllPagesAsync(options, channel);

        Assert.Equal(3, firstPass.TotalCount);
        Assert.Equal(3, firstPass.Items.Count);
        Assert.Equal(
            firstPass.Items.Select(item => item.ComponentId),
            secondPass.Items.Select(item => item.ComponentId));
        Assert.Equal(3, firstPass.Items.Select(item => item.ComponentId).Distinct().Count());
        Assert.DoesNotContain(firstPass.Items, item => item.ComponentId == activeOnly.Id);
        Assert.DoesNotContain(firstPass.Items, item => item.ComponentId == otherRuntime.Id);
        Assert.DoesNotContain(firstPass.Items, item => item.ComponentId == otherChannel.Id);
        Assert.All(firstPass.Items, item => Assert.Single(item.Versions));
        Assert.Equal(
            ["Archived", "DeleteFailed", "Deleted"],
            firstPass.Items
                .SelectMany(item => item.Versions)
                .Select(version => version.Status)
                .OrderBy(status => status, StringComparer.Ordinal)
                .ToArray());

        Assert.Contains(
            interceptor.CommandTexts,
            sql => sql.Contains("edge_client_release_components", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<(IReadOnlyList<ClientReleaseHistoryComponentReadItem> Items, int TotalCount)>
        ReadAllPagesAsync(
            DbContextOptions<IIoTDbContext> options,
            string channel)
    {
        var items = new List<ClientReleaseHistoryComponentReadItem>();
        var totalCount = 0;
        for (var offset = 0; offset < 4; offset += 2)
        {
            await using var context = new IIoTDbContext(options);
            var service = new ClientReleaseHistoryQueryService(context);
            var page = await service.GetPagedAsync(
                new ClientReleaseHistoryQueryRequest(channel, "win-x64", offset, 2));
            totalCount = page.TotalCount;
            items.AddRange(page.Items);
        }

        return (items, totalCount);
    }

    private static ClientReleaseComponent CreatePluginComponent(
        string modulePrefix,
        string channel,
        string targetRuntime,
        ClientReleaseStatus status)
    {
        var moduleId = $"{modulePrefix}{Guid.NewGuid():N}"[..Math.Min(32, modulePrefix.Length + 8)];
        var component = ClientReleaseComponent.CreatePlugin(
            moduleId,
            modulePrefix,
            null,
            null,
            null,
            channel,
            targetRuntime);
        component.UpsertPluginVersion(
            "1.0.0",
            "1.0.0",
            "1.0.0",
            "99.0.0",
            "net10.0",
            $"/edge-updates/plugins/{channel}/{moduleId}/1.0.0/plugin.zip",
            new string('a', 64),
            128,
            "history paging",
            "[]",
            status,
            null,
            "IIoT");
        return component;
    }

    private static ClientReleaseVersion SingleVersion(ClientReleaseComponent component)
        => Assert.Single(component.Versions);

    private static Task SetHistoryTimestampAsync(
        IIoTDbContext context,
        ClientReleaseVersion version,
        DateTime timestamp,
        bool deleted)
        => context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE edge_client_release_versions
            SET created_at_utc = {timestamp},
                published_at_utc = {timestamp},
                deleted_at_utc = {(
                    deleted
                        ? timestamp
                        : (DateTime?)null)}
            WHERE id = {version.Id}
            """);

    private sealed class RecordingCommandInterceptor : DbCommandInterceptor
    {
        public List<string> CommandTexts { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            CommandTexts.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
