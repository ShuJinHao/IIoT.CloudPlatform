using System.Text.Json;
using System.Text.Json.Nodes;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IIoT.EntityFrameworkCore.ClientReleases;

public sealed class EfEdgeInstallerGenerationStore(
    DbContextOptions<IIoTDbContext> dbContextOptions,
    ILogger<EfEdgeInstallerGenerationStore> logger)
    : IEdgeInstallerGenerationStore
{
    private static readonly EventId PersistenceFailed = new(4311, nameof(PersistenceFailed));

    public async Task<bool> TryAddConfirmedAsync(
        EdgeInstallerGenerationRecord record,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var strategyContext = new IIoTDbContext(dbContextOptions);
            var strategy = strategyContext.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(
                callbackToken => WriteAttemptAsync(record, callbackToken),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                PersistenceFailed,
                "Installer generation record persistence failed; ErrorType={ErrorType}.",
                exception.GetType().Name);
            return await ObserveCommitOutcomeAsync(record);
        }
    }

    public async Task<EdgeInstallerGenerationRecord?> GetByIdAsync(
        Guid generationId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = new IIoTDbContext(dbContextOptions);
        return await dbContext.EdgeInstallerGenerationRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.Id == generationId, cancellationToken);
    }

    private async Task<bool> WriteAttemptAsync(
        EdgeInstallerGenerationRecord candidate,
        CancellationToken cancellationToken)
    {
        await using var dbContext = new IIoTDbContext(dbContextOptions);
        var existing = await dbContext.EdgeInstallerGenerationRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.Id == candidate.Id, cancellationToken);
        if (existing is not null)
        {
            return Matches(existing, candidate);
        }

        dbContext.EdgeInstallerGenerationRecords.Add(candidate);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<bool> ObserveCommitOutcomeAsync(EdgeInstallerGenerationRecord candidate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            var existing = await GetByIdAsync(candidate.Id, timeout.Token);
            return existing is not null && Matches(existing, candidate);
        }
        catch (Exception exception)
        {
            logger.LogError(
                PersistenceFailed,
                "Installer generation record verification failed; ErrorType={ErrorType}.",
                exception.GetType().Name);
            return false;
        }
    }

    private static bool Matches(
        EdgeInstallerGenerationRecord existing,
        EdgeInstallerGenerationRecord candidate)
        => existing.Id == candidate.Id
           && existing.OperatorUserId == candidate.OperatorUserId
           && existing.OperatorName == candidate.OperatorName
           && existing.GeneratedAtUtc == candidate.GeneratedAtUtc
           && existing.Channel == candidate.Channel
           && existing.TargetRuntime == candidate.TargetRuntime
           && existing.HostVersion == candidate.HostVersion
           && existing.HostSha256 == candidate.HostSha256
           && existing.FileName == candidate.FileName
           && existing.PackageSha256 == candidate.PackageSha256
           && existing.PackageSize == candidate.PackageSize
           && JsonEquivalent(existing.BindingsJson, candidate.BindingsJson)
           && JsonEquivalent(existing.PluginsJson, candidate.PluginsJson);

    private static bool JsonEquivalent(string existing, string candidate)
    {
        try
        {
            return JsonNode.DeepEquals(
                JsonNode.Parse(existing),
                JsonNode.Parse(candidate));
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
