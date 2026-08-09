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

    public Task<bool> TryAddConfirmedAsync(
        EdgeInstallerGenerationRecord record,
        CancellationToken cancellationToken = default)
        => TryAddConfirmedAsync(record, [], cancellationToken);

    public async Task<bool> TryAddConfirmedAsync(
        EdgeInstallerGenerationRecord record,
        IReadOnlyCollection<EdgeInstallerPendingCredential> pendingCredentials,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var strategyContext = new IIoTDbContext(dbContextOptions);
            var strategy = strategyContext.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(
                callbackToken => WriteAttemptAsync(
                    record,
                    pendingCredentials,
                    callbackToken),
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

    public async Task<IReadOnlyList<EdgeInstallerPendingCredential>> GetPendingByClientCodeAsync(
        string clientCode,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = new IIoTDbContext(dbContextOptions);
        await ExpireOverdueAsync(dbContext, nowUtc, cancellationToken);
        var normalized = clientCode.Trim().ToUpperInvariant();
        return await dbContext.EdgeInstallerPendingCredentials
            .AsNoTracking()
            .Where(item => item.ClientCode == normalized
                           && item.Status != EdgeInstallerPendingCredentialStatus.Activated
                           && item.Status != EdgeInstallerPendingCredentialStatus.Expired
                           && item.Status != EdgeInstallerPendingCredentialStatus.Failed
                           && item.ExpiresAtUtc > nowUtc)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<EdgeInstallerPendingCredential?> GetPendingAsync(
        Guid generationId,
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = new IIoTDbContext(dbContextOptions);
        var pending = await dbContext.EdgeInstallerPendingCredentials
            .SingleOrDefaultAsync(item =>
                item.GenerationId == generationId
                && item.DeviceId == deviceId,
                cancellationToken);
        if (pending is not null && pending.MarkExpired(DateTime.UtcNow))
            await dbContext.SaveChangesAsync(cancellationToken);
        return pending;
    }

    public async Task<EdgeInstallerActivationAttempt> TryActivateAsync(
        Guid generationId,
        Guid deviceId,
        int processId,
        DateTime readyAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = new IIoTDbContext(dbContextOptions);
        var pending = await dbContext.EdgeInstallerPendingCredentials
            .SingleOrDefaultAsync(item =>
                item.GenerationId == generationId
                && item.DeviceId == deviceId,
                cancellationToken);
        if (pending is null)
            return EdgeInstallerActivationAttempt.Unavailable;

        var attempt = pending.BeginActivation(processId, readyAtUtc);
        if (attempt is EdgeInstallerActivationAttempt.Conflict
            or EdgeInstallerActivationAttempt.AlreadyConfirmed
            or EdgeInstallerActivationAttempt.Unavailable)
            return attempt;
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken) > 0
                ? attempt
                : EdgeInstallerActivationAttempt.Unavailable;
        }
        catch (DbUpdateConcurrencyException)
        {
            return EdgeInstallerActivationAttempt.Conflict;
        }
    }

    public async Task<EdgeInstallerActivationAttempt> ConfirmActivationAsync(
        Guid generationId,
        Guid deviceId,
        int processId,
        DateTime readyAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = new IIoTDbContext(dbContextOptions);
        var pending = await dbContext.EdgeInstallerPendingCredentials
            .SingleOrDefaultAsync(item =>
                item.GenerationId == generationId
                && item.DeviceId == deviceId,
                cancellationToken);
        if (pending is null)
            return EdgeInstallerActivationAttempt.Unavailable;

        var attempt = pending.ConfirmActivation(processId, readyAtUtc);
        if (attempt is EdgeInstallerActivationAttempt.Conflict
            or EdgeInstallerActivationAttempt.Unavailable)
            return attempt;
        if (attempt == EdgeInstallerActivationAttempt.AlreadyConfirmed)
            return attempt;

        var device = await dbContext.Devices.SingleOrDefaultAsync(
            item => item.Id == deviceId,
            cancellationToken);
        if (device is null)
            return EdgeInstallerActivationAttempt.Unavailable;
        device.DisableLegacyBootstrap();

        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken) > 0
                ? attempt
                : EdgeInstallerActivationAttempt.Unavailable;
        }
        catch (DbUpdateConcurrencyException)
        {
            return EdgeInstallerActivationAttempt.Conflict;
        }
    }

    public async Task<bool> HasDownloadablePendingAsync(
        Guid generationId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = new IIoTDbContext(dbContextOptions);
        await ExpireOverdueAsync(dbContext, nowUtc, cancellationToken);
        return await dbContext.EdgeInstallerPendingCredentials
            .AsNoTracking()
            .AnyAsync(item =>
                item.GenerationId == generationId
                && item.Status != EdgeInstallerPendingCredentialStatus.Activated
                && item.Status != EdgeInstallerPendingCredentialStatus.Expired
                && item.Status != EdgeInstallerPendingCredentialStatus.Failed
                && item.ExpiresAtUtc > nowUtc,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetPackageCleanupCandidateIdsAsync(
        DateTime nowUtc,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var boundedLimit = Math.Clamp(limit, 1, 1000);
        await using var dbContext = new IIoTDbContext(dbContextOptions);
        return await dbContext.EdgeInstallerGenerationRecords
            .AsNoTracking()
            .Where(record => dbContext.EdgeInstallerPendingCredentials.Any(
                credential => credential.GenerationId == record.Id))
            .Where(record => !dbContext.EdgeInstallerPendingCredentials.Any(
                credential => credential.GenerationId == record.Id
                              && (credential.Status == EdgeInstallerPendingCredentialStatus.Pending
                                  || credential.Status == EdgeInstallerPendingCredentialStatus.Activating)
                              && credential.ExpiresAtUtc > nowUtc))
            .OrderBy(record => record.GeneratedAtUtc)
            .Select(record => record.Id)
            .Take(boundedLimit)
            .ToListAsync(cancellationToken);
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

    public async Task<IReadOnlyList<EdgeInstallerGenerationRecord>> GetRecentByDeviceAsync(
        Guid deviceId,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (deviceId == Guid.Empty)
            return [];
        var boundedLimit = limit <= 0 ? 1 : limit;
        await using var dbContext = new IIoTDbContext(dbContextOptions);
        var candidates = await dbContext.EdgeInstallerGenerationRecords
            .AsNoTracking()
            .OrderByDescending(record => record.GeneratedAtUtc)
            .ToListAsync(cancellationToken);
        return candidates
            .Where(record => ContainsDevice(record.BindingsJson, deviceId))
            .Take(boundedLimit)
            .ToArray();
    }

    private static async Task ExpireOverdueAsync(
        IIoTDbContext dbContext,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var overdue = await dbContext.EdgeInstallerPendingCredentials
            .Where(item =>
                item.Status == EdgeInstallerPendingCredentialStatus.Pending
                && item.ExpiresAtUtc <= nowUtc)
            .ToListAsync(cancellationToken);
        foreach (var pending in overdue)
            pending.MarkExpired(nowUtc);
        if (overdue.Count > 0)
            await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> WriteAttemptAsync(
        EdgeInstallerGenerationRecord candidate,
        IReadOnlyCollection<EdgeInstallerPendingCredential> pendingCredentials,
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
        dbContext.EdgeInstallerPendingCredentials.AddRange(pendingCredentials);
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

    private static bool ContainsDevice(string bindingsJson, Guid deviceId)
    {
        try
        {
            var facts = JsonSerializer.Deserialize<
                EdgeInstallerGenerationBindingFact[]>(bindingsJson);
            return facts?.Any(fact => fact.DeviceId == deviceId) == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
