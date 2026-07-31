using System.Security.Cryptography;
using System.Text;
using IIoT.EntityFrameworkCore.Persistence;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Result;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IIoT.EntityFrameworkCore.Identity;

public sealed class EfRefreshTokenService(
    IIoTDbContext dbContext,
    IOptions<RefreshTokenOptions> refreshTokenOptions) : IRefreshTokenService
{
    private const string SessionLimitRevokedReason = "session-limit";
    private const string HumanTokenVersionPrefix = "h1";
    private const string RotationRevokedReason = "rotated";
    private readonly RefreshTokenOptions _options = refreshTokenOptions.Value;
    private readonly Func<IIoTDbContext> _createContext = dbContext.CreateFreshContext;

    public Task<RefreshTokenEnvelope> IssueHumanAsync(
        Guid subjectId,
        string identityStatusVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityStatusVersion);
        return IssueCoreAsync(
            IIoTClaimTypes.HumanActor,
            subjectId,
            identityStatusVersion,
            requireDevice: false,
            cancellationToken);
    }

    public Task<RefreshTokenEnvelope> IssueAsync(
        string actorType,
        Guid subjectId,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(actorType, IIoTClaimTypes.HumanActor, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Human refresh tokens must be issued with an identity status version.");
        }

        return IssueCoreAsync(
            actorType,
            subjectId,
            identityStatusVersion: null,
            requireDevice: string.Equals(
                actorType,
                IIoTClaimTypes.EdgeDeviceActor,
                StringComparison.Ordinal),
            cancellationToken);
    }

    public async Task<Result<RefreshTokenRotationResult>> RotateAsync(
        string actorType,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tokenHash = ComputeTokenHash(refreshToken);
        var now = NormalizeTimestamp(DateTimeOffset.UtcNow);
        RefreshTokenSession baseline;
        try
        {
            await using var preflightContext = _createContext();
            baseline = await preflightContext.RefreshTokenSessions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    session => session.ActorType == actorType
                               && session.TokenHash == tokenHash,
                    cancellationToken)
                ?? throw new InvalidRefreshTokenException();
        }
        catch (InvalidRefreshTokenException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return InvalidRotationResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw new CloudWriteCommitUnknownException();
        }

        if (baseline.RevokedAtUtc.HasValue || baseline.ExpiresAtUtc <= now)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return InvalidRotationResult();
        }

        var identityStatusVersion = string.Equals(
            actorType,
            IIoTClaimTypes.HumanActor,
            StringComparison.Ordinal)
            ? TryReadHumanIdentityStatusVersion(refreshToken)
            : null;
        if (string.Equals(actorType, IIoTClaimTypes.HumanActor, StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(identityStatusVersion))
        {
            await RevokeInvalidHumanTokenAsync(
                baseline,
                now,
                "status-version-missing",
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return InvalidRotationResult();
        }

        var replacementToken = GenerateToken(identityStatusVersion);
        var replacementSession = CreateSession(
            Guid.NewGuid(),
            actorType,
            baseline.SubjectId,
            replacementToken,
            now);
        var target = new RotationTarget(
            actorType,
            tokenHash,
            baseline.Id,
            baseline.SubjectId,
            baseline.RowVersion,
            now,
            replacementSession);
        var success = Result.Success(new RefreshTokenRotationResult(
            actorType,
            baseline.SubjectId,
            new RefreshTokenEnvelope(replacementToken, replacementSession.ExpiresAtUtc),
            identityStatusVersion));

        Result<RefreshTokenRotationResult> result;
        try
        {
            await using var strategyContext = _createContext();
            var strategy = strategyContext.Database.CreateExecutionStrategy();
            result = await strategy.ExecuteAsync(
                callbackToken => RotateAttemptAsync(
                    target,
                    success,
                    callbackToken),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CompetingRefreshTokenRotationException)
        {
            result = InvalidRotationResult();
        }
        catch
        {
            result = await ObserveRotationOutcomeAsync(target, success);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    public async Task RevokeSubjectTokensAsync(
        string actorType,
        Guid subjectId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        cancellationToken.ThrowIfCancellationRequested();
        var revokedAtUtc = NormalizeTimestamp(DateTimeOffset.UtcNow);
        IReadOnlyList<RevocationSessionTarget> targets;
        try
        {
            await using var preflightContext = _createContext();
            targets = await preflightContext.RefreshTokenSessions
                .AsNoTracking()
                .Where(session =>
                    session.ActorType == actorType
                    && session.SubjectId == subjectId
                    && !session.RevokedAtUtc.HasValue
                    && session.ExpiresAtUtc > revokedAtUtc)
                .OrderBy(session => session.Id)
                .Select(session => new RevocationSessionTarget(
                    session.Id,
                    session.RowVersion))
                .ToListAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw new CloudWriteCommitUnknownException();
        }

        if (targets.Count == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        var target = new SubjectRevocationTarget(
            actorType,
            subjectId,
            reason.Trim(),
            revokedAtUtc,
            true,
            targets);
        try
        {
            await using var strategyContext = _createContext();
            var strategy = strategyContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(
                callbackToken => RevokeSubjectAttemptAsync(target, callbackToken),
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
            await ObserveSubjectRevocationOutcomeAsync(target);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task<RefreshTokenEnvelope> IssueCoreAsync(
        string actorType,
        Guid subjectId,
        string? identityStatusVersion,
        bool requireDevice,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = NormalizeTimestamp(DateTimeOffset.UtcNow);
        var token = GenerateToken(identityStatusVersion);
        var session = CreateSession(
            Guid.NewGuid(),
            actorType,
            subjectId,
            token,
            now);
        var envelope = new RefreshTokenEnvelope(token, session.ExpiresAtUtc);
        try
        {
            await using var strategyContext = _createContext();
            var strategy = strategyContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(
                callbackToken => IssueAttemptAsync(
                    session,
                    requireDevice,
                    callbackToken),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RefreshTokenSubjectUnavailableException)
        {
            throw new InvalidOperationException(
                "Edge device refresh session cannot be issued because the device no longer exists.");
        }
        catch (CloudWriteConflictException)
        {
            throw;
        }
        catch
        {
            await ObserveIssueOutcomeAsync(session);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return envelope;
    }

    private async Task IssueAttemptAsync(
        RefreshTokenSession target,
        bool requireDevice,
        CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        await using var transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);

        if (requireDevice)
        {
            await DeviceDeletionTransactionLock.AcquireAsync(
                context,
                target.SubjectId,
                cancellationToken);
            if (!await context.Devices
                    .AsNoTracking()
                    .AnyAsync(device => device.Id == target.SubjectId, cancellationToken))
            {
                throw new RefreshTokenSubjectUnavailableException();
            }
        }
        else if (string.Equals(
                     target.ActorType,
                     IIoTClaimTypes.HumanActor,
                     StringComparison.Ordinal))
        {
            await RefreshTokenSubjectTransactionLock.AcquireAsync(
                context,
                target.SubjectId,
                cancellationToken);
        }

        var committed = await context.RefreshTokenSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                session => session.Id == target.Id,
                cancellationToken);
        if (committed is not null)
        {
            if (!MatchesSession(committed, target))
            {
                throw new CloudWriteConflictException();
            }

            await transaction.CommitAsync(cancellationToken);
            return;
        }

        if (await context.RefreshTokenSessions
                .AsNoTracking()
                .AnyAsync(
                    session => session.TokenHash == target.TokenHash,
                    cancellationToken))
        {
            throw new CloudWriteConflictException();
        }

        if (string.Equals(
                target.ActorType,
                IIoTClaimTypes.HumanActor,
                StringComparison.Ordinal))
        {
            await RevokeOverflowHumanSessionsAsync(
                context,
                target.SubjectId,
                target.CreatedAtUtc,
                cancellationToken);
        }

        context.RefreshTokenSessions.Add(target);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Result<RefreshTokenRotationResult>> RotateAttemptAsync(
        RotationTarget target,
        Result<RefreshTokenRotationResult> success,
        CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        await using var transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);

        if (string.Equals(
                target.ActorType,
                IIoTClaimTypes.EdgeDeviceActor,
                StringComparison.Ordinal))
        {
            await DeviceDeletionTransactionLock.AcquireAsync(
                context,
                target.SubjectId,
                cancellationToken);
            if (!await context.Devices
                    .AsNoTracking()
                    .AnyAsync(device => device.Id == target.SubjectId, cancellationToken))
            {
                throw new CompetingRefreshTokenRotationException();
            }
        }
        else if (string.Equals(
                     target.ActorType,
                     IIoTClaimTypes.HumanActor,
                     StringComparison.Ordinal))
        {
            await RefreshTokenSubjectTransactionLock.AcquireAsync(
                context,
                target.SubjectId,
                cancellationToken);
        }

        var source = await context.RefreshTokenSessions
            .SingleOrDefaultAsync(
                session => session.ActorType == target.ActorType
                           && session.TokenHash == target.SourceTokenHash,
                cancellationToken);
        if (source is null || source.Id != target.SourceSessionId)
        {
            throw new CompetingRefreshTokenRotationException();
        }

        if (source.RevokedAtUtc.HasValue || source.ExpiresAtUtc <= target.RotatedAtUtc)
        {
            if (!MatchesRotationSource(source, target))
            {
                throw new CompetingRefreshTokenRotationException();
            }

            var replacement = await context.RefreshTokenSessions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    session => session.Id == target.ReplacementSession.Id,
                    cancellationToken);
            if (replacement is null || !MatchesSession(replacement, target.ReplacementSession))
            {
                throw new CloudWriteCommitUnknownException();
            }

            await transaction.CommitAsync(cancellationToken);
            return success;
        }

        if (source.RowVersion != target.SourceRowVersion)
        {
            throw new CompetingRefreshTokenRotationException();
        }

        source.RevokedAtUtc = target.RotatedAtUtc;
        source.RevokedReason = RotationRevokedReason;
        source.ReplacedByTokenId = target.ReplacementSession.Id;
        context.RefreshTokenSessions.Add(target.ReplacementSession);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return success;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CompetingRefreshTokenRotationException();
        }
    }

    private async Task RevokeSubjectAttemptAsync(
        SubjectRevocationTarget target,
        CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        await using var transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        if (string.Equals(
                target.ActorType,
                IIoTClaimTypes.EdgeDeviceActor,
                StringComparison.Ordinal))
        {
            await DeviceDeletionTransactionLock.AcquireAsync(
                context,
                target.SubjectId,
                cancellationToken);
        }
        else if (string.Equals(
                     target.ActorType,
                     IIoTClaimTypes.HumanActor,
                     StringComparison.Ordinal))
        {
            await RefreshTokenSubjectTransactionLock.AcquireAsync(
                context,
                target.SubjectId,
                cancellationToken);
        }

        var targetIds = target.Sessions.Select(session => session.Id).ToArray();
        if (target.RequireCompleteSubject)
        {
            var currentSessions = await context.RefreshTokenSessions
                .AsNoTracking()
                .Where(session =>
                    session.ActorType == target.ActorType
                    && session.SubjectId == target.SubjectId
                    && !session.RevokedAtUtc.HasValue)
                .OrderBy(session => session.Id)
                .ToArrayAsync(cancellationToken);
            var currentTargetIds = currentSessions
                .Where(session => session.ExpiresAtUtc > target.RevokedAtUtc)
                .Select(session => session.Id)
                .ToArray();
            if (currentTargetIds.Any(id => !targetIds.Contains(id)))
            {
                throw new CloudWriteConflictException();
            }
        }

        var sessions = await context.RefreshTokenSessions
            .Where(session => targetIds.Contains(session.Id))
            .ToListAsync(cancellationToken);
        if (sessions.Count != targetIds.Length)
        {
            throw new CloudWriteConflictException();
        }

        foreach (var sessionTarget in target.Sessions)
        {
            var session = sessions.Single(candidate => candidate.Id == sessionTarget.Id);
            if (MatchesRevocationTarget(session, target))
            {
                continue;
            }

            if (session.RowVersion != sessionTarget.RowVersion
                || session.RevokedAtUtc.HasValue
                || !string.Equals(session.ActorType, target.ActorType, StringComparison.Ordinal)
                || session.SubjectId != target.SubjectId)
            {
                throw new CloudWriteConflictException();
            }

            session.RevokedAtUtc = target.RevokedAtUtc;
            session.RevokedReason = target.Reason;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CloudWriteConflictException();
        }
    }

    private async Task RevokeInvalidHumanTokenAsync(
        RefreshTokenSession baseline,
        DateTimeOffset revokedAtUtc,
        string reason,
        CancellationToken cancellationToken)
    {
        var target = new SubjectRevocationTarget(
            baseline.ActorType,
            baseline.SubjectId,
            reason,
            revokedAtUtc,
            false,
            [new RevocationSessionTarget(baseline.Id, baseline.RowVersion)]);
        try
        {
            await using var strategyContext = _createContext();
            var strategy = strategyContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(
                callbackToken => RevokeSubjectAttemptAsync(target, callbackToken),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CloudWriteConflictException)
        {
            return;
        }
        catch
        {
            await ObserveSubjectRevocationOutcomeAsync(target);
        }
    }

    private async Task RevokeOverflowHumanSessionsAsync(
        IIoTDbContext context,
        Guid subjectId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (_options.HumanMaxActiveSessions <= 0)
        {
            return;
        }

        var activeQuery = context.RefreshTokenSessions
            .Where(session =>
                session.ActorType == IIoTClaimTypes.HumanActor
                && session.SubjectId == subjectId
                && !session.RevokedAtUtc.HasValue
                && session.ExpiresAtUtc > now);
        var overflowCount = await activeQuery.CountAsync(cancellationToken)
                            - _options.HumanMaxActiveSessions
                            + 1;
        if (overflowCount <= 0)
        {
            return;
        }

        var sessionsToRevoke = await activeQuery
            .OrderBy(session => session.CreatedAtUtc)
            .ThenBy(session => session.Id)
            .Take(overflowCount)
            .ToListAsync(cancellationToken);
        foreach (var session in sessionsToRevoke)
        {
            session.RevokedAtUtc = now;
            session.RevokedReason = SessionLimitRevokedReason;
        }
    }

    private async Task ObserveIssueOutcomeAsync(RefreshTokenSession target)
    {
        using var observationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await using var context = _createContext();
            var committed = await context.RefreshTokenSessions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    session => session.Id == target.Id,
                    observationTimeout.Token);
            if (committed is null)
            {
                throw new CloudWriteCommitUnknownException();
            }

            if (!MatchesSession(committed, target))
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

    private async Task<Result<RefreshTokenRotationResult>> ObserveRotationOutcomeAsync(
        RotationTarget target,
        Result<RefreshTokenRotationResult> success)
    {
        using var observationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await using var context = _createContext();
            var source = await context.RefreshTokenSessions
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    session => session.Id == target.SourceSessionId,
                    observationTimeout.Token);
            if (source is null)
            {
                return InvalidRotationResult();
            }

            if (MatchesRotationSource(source, target))
            {
                var replacement = await context.RefreshTokenSessions
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        session => session.Id == target.ReplacementSession.Id,
                        observationTimeout.Token);
                if (replacement is not null
                    && MatchesSession(replacement, target.ReplacementSession))
                {
                    return success;
                }

                throw new CloudWriteCommitUnknownException();
            }

            if (source.RevokedAtUtc.HasValue
                || source.RowVersion != target.SourceRowVersion)
            {
                return InvalidRotationResult();
            }

            throw new CloudWriteCommitUnknownException();
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

    private async Task ObserveSubjectRevocationOutcomeAsync(
        SubjectRevocationTarget target)
    {
        using var observationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await using var context = _createContext();
            var targetIds = target.Sessions.Select(session => session.Id).ToArray();
            var sessions = await context.RefreshTokenSessions
                .AsNoTracking()
                .Where(session => targetIds.Contains(session.Id))
                .ToListAsync(observationTimeout.Token);
            if (sessions.Count != targetIds.Length)
            {
                throw new CloudWriteConflictException();
            }

            foreach (var sessionTarget in target.Sessions)
            {
                var session = sessions.Single(candidate => candidate.Id == sessionTarget.Id);
                if (MatchesRevocationTarget(session, target))
                {
                    continue;
                }

                if (!session.RevokedAtUtc.HasValue
                    && session.RowVersion == sessionTarget.RowVersion)
                {
                    throw new CloudWriteCommitUnknownException();
                }

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

    private RefreshTokenSession CreateSession(
        Guid sessionId,
        string actorType,
        Guid subjectId,
        string token,
        DateTimeOffset now)
        => new()
        {
            Id = sessionId,
            ActorType = actorType,
            SubjectId = subjectId,
            TokenHash = ComputeTokenHash(token),
            CreatedAtUtc = now,
            ExpiresAtUtc = NormalizeTimestamp(now.AddDays(ResolveTtlDays(actorType)))
        };

    private int ResolveTtlDays(string actorType)
        => string.Equals(actorType, IIoTClaimTypes.EdgeDeviceActor, StringComparison.Ordinal)
            ? _options.EdgeBootstrapTtlDays
            : _options.HumanTtlDays;

    private static bool MatchesSession(
        RefreshTokenSession persisted,
        RefreshTokenSession target)
        => persisted.Id == target.Id
           && string.Equals(persisted.ActorType, target.ActorType, StringComparison.Ordinal)
           && persisted.SubjectId == target.SubjectId
           && string.Equals(persisted.TokenHash, target.TokenHash, StringComparison.Ordinal)
           && persisted.CreatedAtUtc == target.CreatedAtUtc
           && persisted.ExpiresAtUtc == target.ExpiresAtUtc
           && persisted.RevokedAtUtc == target.RevokedAtUtc
           && string.Equals(persisted.RevokedReason, target.RevokedReason, StringComparison.Ordinal)
           && persisted.ReplacedByTokenId == target.ReplacedByTokenId;

    private static bool MatchesRotationSource(
        RefreshTokenSession source,
        RotationTarget target)
        => source.Id == target.SourceSessionId
           && source.SubjectId == target.SubjectId
           && string.Equals(source.ActorType, target.ActorType, StringComparison.Ordinal)
           && string.Equals(source.TokenHash, target.SourceTokenHash, StringComparison.Ordinal)
           && source.RevokedAtUtc == target.RotatedAtUtc
           && string.Equals(source.RevokedReason, RotationRevokedReason, StringComparison.Ordinal)
           && source.ReplacedByTokenId == target.ReplacementSession.Id;

    private static bool MatchesRevocationTarget(
        RefreshTokenSession session,
        SubjectRevocationTarget target)
        => string.Equals(session.ActorType, target.ActorType, StringComparison.Ordinal)
           && session.SubjectId == target.SubjectId
           && session.RevokedAtUtc == target.RevokedAtUtc
           && string.Equals(session.RevokedReason, target.Reason, StringComparison.Ordinal);

    private static Result<RefreshTokenRotationResult> InvalidRotationResult()
        => Result.Unauthorized("Refresh token is invalid or expired.");

    private static string GenerateToken(string? identityStatusVersion)
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(64));
        if (string.IsNullOrWhiteSpace(identityStatusVersion))
        {
            return secret;
        }

        var encodedStatusVersion = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(identityStatusVersion))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"{HumanTokenVersionPrefix}.{encodedStatusVersion}.{secret}";
    }

    private static string? TryReadHumanIdentityStatusVersion(string token)
    {
        var segments = token.Split('.', 3, StringSplitOptions.None);
        if (segments.Length != 3
            || !string.Equals(segments[0], HumanTokenVersionPrefix, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(segments[1])
            || string.IsNullOrWhiteSpace(segments[2]))
        {
            return null;
        }

        try
        {
            var encoded = segments[1]
                .Replace('-', '+')
                .Replace('_', '/');
            var padding = encoded.Length % 4;
            if (padding > 0)
            {
                encoded = encoded.PadRight(encoded.Length + 4 - padding, '=');
            }

            return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string ComputeTokenHash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
    }

    private sealed record RotationTarget(
        string ActorType,
        string SourceTokenHash,
        Guid SourceSessionId,
        Guid SubjectId,
        uint SourceRowVersion,
        DateTimeOffset RotatedAtUtc,
        RefreshTokenSession ReplacementSession);

    private sealed record RevocationSessionTarget(Guid Id, uint RowVersion);

    private sealed record SubjectRevocationTarget(
        string ActorType,
        Guid SubjectId,
        string Reason,
        DateTimeOffset RevokedAtUtc,
        bool RequireCompleteSubject,
        IReadOnlyList<RevocationSessionTarget> Sessions);

    private sealed class InvalidRefreshTokenException : Exception
    {
    }

    private sealed class CompetingRefreshTokenRotationException : Exception
    {
    }

    private sealed class RefreshTokenSubjectUnavailableException : Exception
    {
    }
}
