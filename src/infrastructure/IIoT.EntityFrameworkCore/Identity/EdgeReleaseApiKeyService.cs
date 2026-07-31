using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IIoT.EntityFrameworkCore.Auditing;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Result;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.Identity;

public sealed class EdgeReleaseApiKeyService(IIoTDbContext dbContext) : IEdgeReleaseApiKeyService
{
    private const string KeyPrefix = "iiot_edge_release_";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] DefaultPermissions =
    [
        ClientReleasePermissions.Read,
        ClientReleasePermissions.Publish
    ];
    private static readonly HashSet<string> AllowedPermissions =
        new(DefaultPermissions, StringComparer.Ordinal);
    private readonly Func<IIoTDbContext> _createContext = dbContext.CreateFreshContext;

    public async Task<Result<EdgeReleaseApiKeyCreateResult>> CreateAsync(
        string name,
        IReadOnlyCollection<string>? permissions,
        DateTimeOffset? expiresAtUtc,
        Guid? createdByUserId,
        EdgeReleaseApiKeyAuditContext auditContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditContext);
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedName = NormalizeName(name);
        if (normalizedName is null)
        {
            return Result.Invalid("发布 API key 名称不能为空。");
        }

        var normalizedPermissions = NormalizePermissions(permissions);
        if (normalizedPermissions is null)
        {
            return Result.Invalid("发布 API key 只能授予 ClientRelease.Read 和 ClientRelease.Publish 权限。");
        }

        var createdAtUtc = NormalizeTimestamp(DateTimeOffset.UtcNow);
        var resolvedExpiresAt = NormalizeTimestamp(expiresAtUtc ?? createdAtUtc.AddYears(1));
        if (resolvedExpiresAt <= createdAtUtc.AddMinutes(5))
        {
            return Result.Invalid("发布 API key 过期时间必须晚于当前时间至少 5 分钟。");
        }

        try
        {
            await using var preflightContext = _createContext();
            if (await preflightContext.EdgeReleaseApiKeys
                    .AsNoTracking()
                    .AnyAsync(key => key.Name == normalizedName, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Result.Invalid($"发布 API key 名称已存在：{normalizedName}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw new CloudWriteCommitUnknownException();
        }

        var apiKey = GenerateApiKey();
        var keyId = Guid.NewGuid();
        var keyHash = ComputeHash(apiKey);
        var permissionsJson = JsonSerializer.Serialize(normalizedPermissions, JsonOptions);
        var auditRecordId = Guid.NewGuid();
        var auditEntry = CreateCreateAuditEntry(
            keyId,
            normalizedName,
            normalizedPermissions.Count,
            resolvedExpiresAt,
            createdByUserId,
            auditContext);
        var target = new CreateTarget(
            keyId,
            normalizedName,
            keyHash,
            permissionsJson,
            resolvedExpiresAt,
            createdAtUtc,
            createdByUserId,
            auditRecordId,
            auditEntry);

        try
        {
            await using var strategyContext = _createContext();
            var strategy = strategyContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(
                callbackToken => CreateAttemptAsync(target, callbackToken),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CloudWriteConflictException)
        {
            throw;
        }
        catch
        {
            await ObserveCreateOutcomeAsync(target);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Result.Success(new EdgeReleaseApiKeyCreateResult(
            keyId,
            normalizedName,
            apiKey,
            resolvedExpiresAt,
            normalizedPermissions));
    }

    public async Task<IReadOnlyList<EdgeReleaseApiKeyListItem>> GetListAsync(
        CancellationToken cancellationToken = default)
    {
        await using var readContext = _createContext();
        var keys = await readContext.EdgeReleaseApiKeys
            .AsNoTracking()
            .OrderByDescending(key => key.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        return keys.Select(ToListItem).ToList();
    }

    public async Task<Result> RevokeAsync(
        Guid id,
        Guid? revokedByUserId,
        string? reason,
        EdgeReleaseApiKeyAuditContext auditContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditContext);
        cancellationToken.ThrowIfCancellationRequested();

        EdgeReleaseApiKey baseline;
        try
        {
            await using var preflightContext = _createContext();
            baseline = await preflightContext.EdgeReleaseApiKeys
                .AsNoTracking()
                .SingleOrDefaultAsync(key => key.Id == id, cancellationToken)
                ?? throw new EdgeReleaseApiKeyNotFoundException();
        }
        catch (EdgeReleaseApiKeyNotFoundException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Result.NotFound("发布 API key 不存在。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw new CloudWriteCommitUnknownException();
        }

        if (baseline.Status == EdgeReleaseApiKeyStatuses.Revoked)
        {
            await EnsurePreviouslyCommittedRevokeAuditAsync(
                baseline,
                revokedByUserId,
                auditContext,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return Result.Success();
        }

        var revokedAtUtc = NormalizeTimestamp(DateTimeOffset.UtcNow);
        var normalizedReason = string.IsNullOrWhiteSpace(reason)
            ? "manual-revoke"
            : reason.Trim();
        var auditRecordId = Guid.NewGuid();
        var auditEntry = CreateRevokeAuditEntry(
            id,
            normalizedReason,
            revokedByUserId,
            auditContext,
            revokedAtUtc);
        var target = new RevokeTarget(
            id,
            baseline.Name,
            baseline.KeyHash,
            baseline.PermissionsJson,
            baseline.ExpiresAtUtc,
            baseline.CreatedAtUtc,
            baseline.CreatedByUserId,
            revokedAtUtc,
            revokedByUserId,
            normalizedReason,
            auditRecordId,
            auditEntry);

        try
        {
            await using var strategyContext = _createContext();
            var strategy = strategyContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(
                callbackToken => RevokeAttemptAsync(target, callbackToken),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CloudWriteConflictException)
        {
            throw;
        }
        catch
        {
            await ObserveRevokeOutcomeAsync(target);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Result.Success();
    }

    public async Task<Result<EdgeReleaseApiKeyValidationResult>> ValidateAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Result.Unauthorized("发布 API key 不能为空。");
        }

        var hash = ComputeHash(apiKey.Trim());
        var usedAtUtc = NormalizeTimestamp(DateTimeOffset.UtcNow);

        Result<EdgeReleaseApiKeyValidationResult> result;
        try
        {
            await using var strategyContext = _createContext();
            var strategy = strategyContext.Database.CreateExecutionStrategy();
            result = await strategy.ExecuteAsync(
                callbackToken => ValidateAttemptAsync(hash, usedAtUtc, callbackToken),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            result = await ObserveValidationOutcomeAsync(hash, usedAtUtc);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private async Task CreateAttemptAsync(
        CreateTarget target,
        CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        var existingById = await context.EdgeReleaseApiKeys
            .SingleOrDefaultAsync(key => key.Id == target.Id, cancellationToken);
        if (existingById is not null)
        {
            if (!MatchesCreateTarget(existingById, target))
            {
                throw new CloudWriteConflictException();
            }

            await EnsureAuditTargetAsync(context, target.AuditRecordId, target.AuditEntry, cancellationToken);
            return;
        }

        if (await context.EdgeReleaseApiKeys
                .AsNoTracking()
                .AnyAsync(
                    key => key.Name == target.Name || key.KeyHash == target.KeyHash,
                    cancellationToken))
        {
            throw new CloudWriteConflictException();
        }

        context.EdgeReleaseApiKeys.Add(new EdgeReleaseApiKey
        {
            Id = target.Id,
            Name = target.Name,
            KeyHash = target.KeyHash,
            Status = EdgeReleaseApiKeyStatuses.Active,
            PermissionsJson = target.PermissionsJson,
            ExpiresAtUtc = target.ExpiresAtUtc,
            CreatedAtUtc = target.CreatedAtUtc,
            CreatedByUserId = target.CreatedByUserId
        });
        context.AuditTrails.Add(AuditTrailRecord.FromEntry(target.AuditRecordId, target.AuditEntry));
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task RevokeAttemptAsync(
        RevokeTarget target,
        CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        await using var transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        var entity = await context.EdgeReleaseApiKeys
            .AsNoTracking()
            .SingleOrDefaultAsync(key => key.Id == target.Id, cancellationToken)
            ?? throw new CloudWriteConflictException();

        if (MatchesRevokeTarget(entity, target))
        {
            await EnsureAuditTargetAsync(context, target.AuditRecordId, target.AuditEntry, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        if (!MatchesRevokeBaseline(entity, target))
        {
            throw new CloudWriteConflictException();
        }

        var updated = await context.EdgeReleaseApiKeys
            .Where(key =>
                key.Id == target.Id
                && key.Name == target.BaselineName
                && key.KeyHash == target.BaselineKeyHash
                && key.PermissionsJson == target.BaselinePermissionsJson
                && key.ExpiresAtUtc == target.BaselineExpiresAtUtc
                && key.CreatedAtUtc == target.BaselineCreatedAtUtc
                && key.CreatedByUserId == target.BaselineCreatedByUserId
                && key.Status == EdgeReleaseApiKeyStatuses.Active
                && !key.RevokedAtUtc.HasValue)
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(
                        key => key.Status,
                        EdgeReleaseApiKeyStatuses.Revoked)
                    .SetProperty(
                        key => key.RevokedAtUtc,
                        target.RevokedAtUtc)
                    .SetProperty(
                        key => key.RevokedByUserId,
                        target.RevokedByUserId)
                    .SetProperty(
                        key => key.RevokedReason,
                        target.RevokedReason),
                cancellationToken);
        if (updated != 1)
        {
            entity = await context.EdgeReleaseApiKeys
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    key => key.Id == target.Id,
                    cancellationToken)
                ?? throw new CloudWriteConflictException();
            if (!MatchesRevokeTarget(entity, target))
            {
                throw new CloudWriteConflictException();
            }
        }

        context.AuditTrails.Add(AuditTrailRecord.FromEntry(target.AuditRecordId, target.AuditEntry));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Result<EdgeReleaseApiKeyValidationResult>> ValidateAttemptAsync(
        string keyHash,
        DateTimeOffset usedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        await using var transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        var entity = await context.EdgeReleaseApiKeys
            .AsNoTracking()
            .SingleOrDefaultAsync(key => key.KeyHash == keyHash, cancellationToken);
        if (!IsUsable(entity, usedAtUtc))
        {
            await transaction.CommitAsync(cancellationToken);
            return InvalidKeyResult();
        }

        for (var compareExchangeAttempt = 0;
             !entity!.LastUsedAtUtc.HasValue || entity.LastUsedAtUtc.Value < usedAtUtc;
             compareExchangeAttempt++)
        {
            var entityId = entity.Id;
            var baselineLastUsedAtUtc = entity.LastUsedAtUtc;
            var baselineExpiresAtUtc = entity.ExpiresAtUtc;
            var updated = await context.EdgeReleaseApiKeys
                .Where(key =>
                    key.Id == entityId
                    && key.KeyHash == keyHash
                    && key.Status == EdgeReleaseApiKeyStatuses.Active
                    && !key.RevokedAtUtc.HasValue
                    && key.ExpiresAtUtc == baselineExpiresAtUtc
                    && key.LastUsedAtUtc == baselineLastUsedAtUtc)
                .ExecuteUpdateAsync(
                    updates => updates.SetProperty(
                        key => key.LastUsedAtUtc,
                        usedAtUtc),
                    cancellationToken);
            entity = await context.EdgeReleaseApiKeys
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    key => key.Id == entityId,
                    cancellationToken);
            if (!IsUsable(entity, usedAtUtc))
            {
                await transaction.CommitAsync(cancellationToken);
                return InvalidKeyResult();
            }

            if ((!entity!.LastUsedAtUtc.HasValue
                 || entity.LastUsedAtUtc.Value < usedAtUtc)
                && (updated > 0 || compareExchangeAttempt >= 2))
            {
                throw new CloudWriteCommitUnknownException();
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return ToValidationResult(entity);
    }

    private async Task ObserveCreateOutcomeAsync(CreateTarget target)
    {
        using var observationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await using var context = _createContext();
            var existing = await context.EdgeReleaseApiKeys
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    key => key.Id == target.Id
                           || key.Name == target.Name
                           || key.KeyHash == target.KeyHash,
                    observationTimeout.Token);
            if (existing is null)
            {
                throw new CloudWriteCommitUnknownException();
            }

            if (!MatchesCreateTarget(existing, target))
            {
                throw new CloudWriteConflictException();
            }

            var audit = await context.AuditTrails
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    record => record.Id == target.AuditRecordId,
                    observationTimeout.Token);
            if (audit is null)
            {
                throw new CloudWriteCommitUnknownException();
            }

            if (!MatchesAuditTarget(audit, target.AuditEntry))
            {
                throw new CloudWriteConflictException();
            }
        }
        catch (CloudWriteException)
        {
            throw;
        }
        catch
        {
            throw new CloudWriteCommitUnknownException();
        }
    }

    private async Task ObserveRevokeOutcomeAsync(RevokeTarget target)
    {
        using var observationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await using var context = _createContext();
            var entity = await context.EdgeReleaseApiKeys
                .AsNoTracking()
                .SingleOrDefaultAsync(key => key.Id == target.Id, observationTimeout.Token);
            if (entity is null)
            {
                throw new CloudWriteConflictException();
            }

            if (!MatchesRevokeTarget(entity, target))
            {
                if (MatchesRevokeBaseline(entity, target))
                {
                    throw new CloudWriteCommitUnknownException();
                }

                throw new CloudWriteConflictException();
            }

            var audit = await context.AuditTrails
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    record => record.Id == target.AuditRecordId,
                    observationTimeout.Token);
            if (audit is null)
            {
                throw new CloudWriteCommitUnknownException();
            }

            if (!MatchesAuditTarget(audit, target.AuditEntry))
            {
                throw new CloudWriteConflictException();
            }
        }
        catch (CloudWriteException)
        {
            throw;
        }
        catch
        {
            throw new CloudWriteCommitUnknownException();
        }
    }

    private async Task EnsurePreviouslyCommittedRevokeAuditAsync(
        EdgeReleaseApiKey revokedKey,
        Guid? currentActorUserId,
        EdgeReleaseApiKeyAuditContext auditContext,
        CancellationToken cancellationToken)
    {
        if (!revokedKey.RevokedAtUtc.HasValue)
        {
            throw new CloudWriteConflictException();
        }

        var revokedAtUtc = NormalizeTimestamp(revokedKey.RevokedAtUtc.Value);
        var revokedReason = string.IsNullOrWhiteSpace(revokedKey.RevokedReason)
            ? "manual-revoke"
            : revokedKey.RevokedReason.Trim();
        var auditEntry = new AuditTrailEntry(
            revokedKey.RevokedByUserId,
            currentActorUserId == revokedKey.RevokedByUserId
                ? auditContext.ActorEmployeeNo
                : null,
            "ClientRelease.ApiKey.Revoke",
            "EdgeReleaseApiKey",
            revokedKey.Id.ToString(),
            revokedAtUtc.UtcDateTime,
            true,
            $"Revoked Edge release API key {revokedKey.Id}. Reason: {revokedReason}.",
            IdempotencyKey:
                $"edge-release-api-key-revoke:{revokedKey.Id:N}:{revokedAtUtc.UtcTicks}");
        var auditRecordId = Guid.NewGuid();

        try
        {
            await using var strategyContext = _createContext();
            var strategy = strategyContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(
                callbackToken => EnsureRevokeAuditByIdempotencyKeyAsync(
                    auditRecordId,
                    auditEntry,
                    callbackToken),
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CloudWriteConflictException)
        {
            throw;
        }
        catch
        {
            await ObserveRevokeAuditOutcomeAsync(auditEntry);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task EnsureRevokeAuditByIdempotencyKeyAsync(
        Guid auditRecordId,
        AuditTrailEntry auditEntry,
        CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        var audit = await context.AuditTrails
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.IdempotencyKey == auditEntry.IdempotencyKey,
                cancellationToken);
        if (audit is not null)
        {
            if (!MatchesRevokeAuditTarget(audit, auditEntry))
            {
                throw new CloudWriteConflictException();
            }

            return;
        }

        context.AuditTrails.Add(
            AuditTrailRecord.FromEntry(auditRecordId, auditEntry));
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task ObserveRevokeAuditOutcomeAsync(
        AuditTrailEntry auditEntry)
    {
        using var observationTimeout =
            new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await using var context = _createContext();
            var audit = await context.AuditTrails
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    record =>
                        record.IdempotencyKey == auditEntry.IdempotencyKey,
                    observationTimeout.Token);
            if (audit is null)
            {
                throw new CloudWriteCommitUnknownException();
            }

            if (!MatchesRevokeAuditTarget(audit, auditEntry))
            {
                throw new CloudWriteConflictException();
            }
        }
        catch (CloudWriteException)
        {
            throw;
        }
        catch
        {
            throw new CloudWriteCommitUnknownException();
        }
    }

    private async Task<Result<EdgeReleaseApiKeyValidationResult>> ObserveValidationOutcomeAsync(
        string keyHash,
        DateTimeOffset usedAtUtc)
    {
        using var observationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await using var context = _createContext();
            var entity = await context.EdgeReleaseApiKeys
                .AsNoTracking()
                .SingleOrDefaultAsync(key => key.KeyHash == keyHash, observationTimeout.Token);
            if (!IsUsable(entity, usedAtUtc))
            {
                return InvalidKeyResult();
            }

            if (!entity!.LastUsedAtUtc.HasValue || entity.LastUsedAtUtc.Value < usedAtUtc)
            {
                throw new CloudWriteCommitUnknownException();
            }

            return ToValidationResult(entity);
        }
        catch (CloudWriteException)
        {
            throw;
        }
        catch
        {
            throw new CloudWriteCommitUnknownException();
        }
    }

    private static async Task EnsureAuditTargetAsync(
        IIoTDbContext context,
        Guid auditRecordId,
        AuditTrailEntry auditEntry,
        CancellationToken cancellationToken)
    {
        var audit = await context.AuditTrails
            .SingleOrDefaultAsync(record => record.Id == auditRecordId, cancellationToken);
        if (audit is not null)
        {
            if (!MatchesAuditTarget(audit, auditEntry))
            {
                throw new CloudWriteConflictException();
            }

            return;
        }

        context.AuditTrails.Add(AuditTrailRecord.FromEntry(auditRecordId, auditEntry));
        await context.SaveChangesAsync(cancellationToken);
    }

    private static AuditTrailEntry CreateCreateAuditEntry(
        Guid keyId,
        string name,
        int permissionCount,
        DateTimeOffset expiresAtUtc,
        Guid? actorUserId,
        EdgeReleaseApiKeyAuditContext auditContext)
        => new(
            actorUserId,
            auditContext.ActorEmployeeNo,
            "ClientRelease.ApiKey.Create",
            "EdgeReleaseApiKey",
            keyId.ToString(),
            NormalizeTimestamp(auditContext.ExecutedAtUtc),
            true,
            $"Created Edge release API key '{name}' with {permissionCount} permission(s), expires at {expiresAtUtc:O}.",
            IdempotencyKey: $"edge-release-api-key-create:{keyId:N}");

    private static AuditTrailEntry CreateRevokeAuditEntry(
        Guid keyId,
        string reason,
        Guid? actorUserId,
        EdgeReleaseApiKeyAuditContext auditContext,
        DateTimeOffset revokedAtUtc)
        => new(
            actorUserId,
            auditContext.ActorEmployeeNo,
            "ClientRelease.ApiKey.Revoke",
            "EdgeReleaseApiKey",
            keyId.ToString(),
            NormalizeTimestamp(auditContext.ExecutedAtUtc),
            true,
            $"Revoked Edge release API key {keyId}. Reason: {reason}.",
            IdempotencyKey: $"edge-release-api-key-revoke:{keyId:N}:{revokedAtUtc.UtcTicks}");

    private static bool MatchesCreateTarget(EdgeReleaseApiKey entity, CreateTarget target)
        => entity.Id == target.Id
           && string.Equals(entity.Name, target.Name, StringComparison.Ordinal)
           && string.Equals(entity.KeyHash, target.KeyHash, StringComparison.Ordinal)
           && string.Equals(entity.Status, EdgeReleaseApiKeyStatuses.Active, StringComparison.Ordinal)
           && JsonPayloadEquals(entity.PermissionsJson, target.PermissionsJson)
           && entity.ExpiresAtUtc == target.ExpiresAtUtc
           && entity.CreatedAtUtc == target.CreatedAtUtc
           && entity.CreatedByUserId == target.CreatedByUserId
           && entity.RevokedAtUtc is null
           && entity.RevokedByUserId is null
           && entity.RevokedReason is null;

    private static bool MatchesRevokeTarget(EdgeReleaseApiKey entity, RevokeTarget target)
        => MatchesRevokeSecurityFields(entity, target)
           && string.Equals(entity.Status, EdgeReleaseApiKeyStatuses.Revoked, StringComparison.Ordinal)
           && entity.RevokedAtUtc == target.RevokedAtUtc
           && entity.RevokedByUserId == target.RevokedByUserId
           && string.Equals(entity.RevokedReason, target.RevokedReason, StringComparison.Ordinal);

    private static bool MatchesRevokeBaseline(
        EdgeReleaseApiKey entity,
        RevokeTarget target)
        => MatchesRevokeSecurityFields(entity, target)
           && string.Equals(
               entity.Status,
               EdgeReleaseApiKeyStatuses.Active,
               StringComparison.Ordinal)
           && entity.RevokedAtUtc is null
           && entity.RevokedByUserId is null
           && entity.RevokedReason is null;

    private static bool MatchesRevokeSecurityFields(
        EdgeReleaseApiKey entity,
        RevokeTarget target)
        => entity.Id == target.Id
           && string.Equals(
               entity.Name,
               target.BaselineName,
               StringComparison.Ordinal)
           && string.Equals(
               entity.KeyHash,
               target.BaselineKeyHash,
               StringComparison.Ordinal)
           && JsonPayloadEquals(
               entity.PermissionsJson,
               target.BaselinePermissionsJson)
           && entity.ExpiresAtUtc == target.BaselineExpiresAtUtc
           && entity.CreatedAtUtc == target.BaselineCreatedAtUtc
           && entity.CreatedByUserId == target.BaselineCreatedByUserId;

    private static bool MatchesAuditTarget(AuditTrailRecord record, AuditTrailEntry entry)
        => record.ActorUserId == entry.ActorUserId
           && string.Equals(record.ActorEmployeeNo, entry.ActorEmployeeNo, StringComparison.Ordinal)
           && string.Equals(record.OperationType, entry.OperationType, StringComparison.Ordinal)
           && string.Equals(record.TargetType, entry.TargetType, StringComparison.Ordinal)
           && string.Equals(record.TargetIdOrKey, entry.TargetIdOrKey, StringComparison.Ordinal)
           && record.ExecutedAtUtc == entry.ExecutedAtUtc
           && record.Succeeded == entry.Succeeded
           && string.Equals(record.Summary, entry.Summary, StringComparison.Ordinal)
           && string.Equals(record.FailureReason, entry.FailureReason, StringComparison.Ordinal)
           && string.Equals(record.IdempotencyKey, entry.IdempotencyKey, StringComparison.Ordinal);

    private static bool MatchesRevokeAuditTarget(
        AuditTrailRecord record,
        AuditTrailEntry entry)
        => record.ActorUserId == entry.ActorUserId
           && string.Equals(
               record.OperationType,
               entry.OperationType,
               StringComparison.Ordinal)
           && string.Equals(
               record.TargetType,
               entry.TargetType,
               StringComparison.Ordinal)
           && string.Equals(
               record.TargetIdOrKey,
               entry.TargetIdOrKey,
               StringComparison.Ordinal)
           && record.Succeeded == entry.Succeeded
           && string.Equals(
               record.Summary,
               entry.Summary,
               StringComparison.Ordinal)
           && string.Equals(
               record.FailureReason,
               entry.FailureReason,
               StringComparison.Ordinal)
           && string.Equals(
               record.IdempotencyKey,
               entry.IdempotencyKey,
               StringComparison.Ordinal);

    private static bool JsonPayloadEquals(string persisted, string target)
    {
        try
        {
            using var persistedDocument = JsonDocument.Parse(persisted);
            using var targetDocument = JsonDocument.Parse(target);
            return JsonElement.DeepEquals(
                persistedDocument.RootElement,
                targetDocument.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsUsable(EdgeReleaseApiKey? entity, DateTimeOffset now)
        => entity is not null
           && string.Equals(entity.Status, EdgeReleaseApiKeyStatuses.Active, StringComparison.Ordinal)
           && !entity.RevokedAtUtc.HasValue
           && entity.ExpiresAtUtc > now;

    private static Result<EdgeReleaseApiKeyValidationResult> ToValidationResult(EdgeReleaseApiKey entity)
        => Result.Success(new EdgeReleaseApiKeyValidationResult(
            entity.Id,
            entity.Name,
            DeserializePermissions(entity.PermissionsJson)));

    private static Result<EdgeReleaseApiKeyValidationResult> InvalidKeyResult()
        => Result.Unauthorized("发布 API key 无效、已吊销或已过期。");

    private static EdgeReleaseApiKeyListItem ToListItem(EdgeReleaseApiKey entity)
        => new(
            entity.Id,
            entity.Name,
            entity.Status,
            entity.ExpiresAtUtc,
            entity.LastUsedAtUtc,
            entity.CreatedAtUtc,
            entity.RevokedAtUtc,
            entity.RevokedReason,
            DeserializePermissions(entity.PermissionsJson));

    private static string? NormalizeName(string name)
    {
        var normalized = name.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static IReadOnlyList<string>? NormalizePermissions(IReadOnlyCollection<string>? permissions)
    {
        var resolved = (permissions is null || permissions.Count == 0
                ? DefaultPermissions
                : permissions)
            .Select(permission => permission.Trim())
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (resolved.Length == 0 || resolved.Any(permission => !AllowedPermissions.Contains(permission)))
        {
            return null;
        }

        return resolved;
    }

    private static IReadOnlyList<string> DeserializePermissions(string permissionsJson)
        => JsonSerializer.Deserialize<string[]>(permissionsJson, JsonOptions) ?? [];

    private static string GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"{KeyPrefix}{token}";
    }

    private static string ComputeHash(string apiKey)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));

    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
    }

    private static DateTime NormalizeTimestamp(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        return new DateTime(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);
    }

    private sealed record CreateTarget(
        Guid Id,
        string Name,
        string KeyHash,
        string PermissionsJson,
        DateTimeOffset ExpiresAtUtc,
        DateTimeOffset CreatedAtUtc,
        Guid? CreatedByUserId,
        Guid AuditRecordId,
        AuditTrailEntry AuditEntry);

    private sealed record RevokeTarget(
        Guid Id,
        string BaselineName,
        string BaselineKeyHash,
        string BaselinePermissionsJson,
        DateTimeOffset BaselineExpiresAtUtc,
        DateTimeOffset BaselineCreatedAtUtc,
        Guid? BaselineCreatedByUserId,
        DateTimeOffset RevokedAtUtc,
        Guid? RevokedByUserId,
        string RevokedReason,
        Guid AuditRecordId,
        AuditTrailEntry AuditEntry);

    private sealed class EdgeReleaseApiKeyNotFoundException : Exception
    {
    }
}
