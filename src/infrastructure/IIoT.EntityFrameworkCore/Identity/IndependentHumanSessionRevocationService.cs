using System.Data;
using IIoT.EntityFrameworkCore.Persistence;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace IIoT.EntityFrameworkCore.Identity;

public sealed class IndependentHumanSessionRevocationService(IIoTDbContext dbContext)
    : IIndependentHumanSessionRevocationService
{
    private readonly Func<IIoTDbContext> _createContext = dbContext.CreateFreshContext;

    public async Task RevokeAllAsync(
        Guid subjectId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        cancellationToken.ThrowIfCancellationRequested();
        var revokedAtUtc = NormalizeTimestamp(DateTimeOffset.UtcNow);
        var normalizedReason = reason.Trim();
        RevocationTarget target;
        try
        {
            target = await ReadTargetAsync(
                subjectId,
                normalizedReason,
                revokedAtUtc,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw new CloudWriteCommitUnknownException();
        }

        if (target.RefreshSessions.Count == 0
            && target.Tokens.Count == 0
            && target.Authorizations.Count == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

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
            await ObserveOutcomeAsync(target);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task<RevocationTarget> ReadTargetAsync(
        Guid subjectId,
        string reason,
        DateTimeOffset revokedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var strategyContext = _createContext();
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        var baseline = await strategy.ExecuteAsync(
            callbackToken => ReadBaselineAttemptAsync(subjectId, callbackToken),
            cancellationToken);
        var subject = subjectId.ToString();
        return new RevocationTarget(
            subjectId,
            subject,
            reason,
            revokedAtUtc,
            baseline.RefreshSessions,
            baseline.Tokens.Select(token => new GrantTarget(
                token.Id,
                token.ConcurrencyToken,
                Guid.NewGuid().ToString("N"))).ToArray(),
            baseline.Authorizations.Select(authorization => new GrantTarget(
                authorization.Id,
                authorization.ConcurrencyToken,
                Guid.NewGuid().ToString("N"))).ToArray());
    }

    private async Task<RevocationBaseline> ReadBaselineAttemptAsync(
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        var subject = subjectId.ToString();
        var refreshSessions = await context.RefreshTokenSessions
            .AsNoTracking()
            .Where(session =>
                session.ActorType == IIoTClaimTypes.HumanActor
                && session.SubjectId == subjectId
                && !session.RevokedAtUtc.HasValue)
            .OrderBy(session => session.Id)
            .Select(session => new RefreshSessionTarget(
                session.Id,
                session.RowVersion))
            .ToListAsync(cancellationToken);
        var tokens = await context.OpenIddictTokens
            .AsNoTracking()
            .Where(token =>
                token.Subject == subject
                && token.Status != OpenIddictConstants.Statuses.Revoked)
            .OrderBy(token => token.Id)
            .Select(token => new GrantBaseline(
                token.Id,
                token.ConcurrencyToken))
            .ToListAsync(cancellationToken);
        var authorizations = await context.OpenIddictAuthorizations
            .AsNoTracking()
            .Where(authorization =>
                authorization.Subject == subject
                && authorization.Status != OpenIddictConstants.Statuses.Revoked)
            .OrderBy(authorization => authorization.Id)
            .Select(authorization => new GrantBaseline(
                authorization.Id,
                authorization.ConcurrencyToken))
            .ToListAsync(cancellationToken);

        var baseline = new RevocationBaseline(
            refreshSessions,
            tokens,
            authorizations);
        await transaction.CommitAsync(cancellationToken);
        return baseline;
    }

    private async Task RevokeAttemptAsync(
        RevocationTarget target,
        CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        await RefreshTokenSubjectTransactionLock.AcquireAsync(
            context,
            target.SubjectId,
            cancellationToken);

        var refreshIds = target.RefreshSessions.Select(session => session.Id).ToArray();
        var tokenIds = target.Tokens.Select(token => token.Id).ToArray();
        var authorizationIds = target.Authorizations
            .Select(authorization => authorization.Id)
            .ToArray();
        var currentRefreshIds = await context.RefreshTokenSessions
            .AsNoTracking()
            .Where(session =>
                session.ActorType == IIoTClaimTypes.HumanActor
                && session.SubjectId == target.SubjectId
                && !session.RevokedAtUtc.HasValue)
            .Select(session => session.Id)
            .ToArrayAsync(cancellationToken);
        var currentTokenIds = await context.OpenIddictTokens
            .AsNoTracking()
            .Where(token =>
                token.Subject == target.Subject
                && token.Status != OpenIddictConstants.Statuses.Revoked)
            .Select(token => token.Id)
            .ToArrayAsync(cancellationToken);
        var currentAuthorizationIds = await context.OpenIddictAuthorizations
            .AsNoTracking()
            .Where(authorization =>
                authorization.Subject == target.Subject
                && authorization.Status != OpenIddictConstants.Statuses.Revoked)
            .Select(authorization => authorization.Id)
            .ToArrayAsync(cancellationToken);
        if (currentRefreshIds.Any(id => !refreshIds.Contains(id))
            || currentTokenIds.Any(id => !tokenIds.Contains(id))
            || currentAuthorizationIds.Any(id => !authorizationIds.Contains(id)))
        {
            throw new CloudWriteConflictException();
        }

        var refreshSessions = refreshIds.Length == 0
            ? []
            : await context.RefreshTokenSessions
                .Where(session => refreshIds.Contains(session.Id))
                .ToListAsync(cancellationToken);
        var tokens = tokenIds.Length == 0
            ? []
            : await context.OpenIddictTokens
                .Where(token => tokenIds.Contains(token.Id))
                .ToListAsync(cancellationToken);
        var authorizations = authorizationIds.Length == 0
            ? []
            : await context.OpenIddictAuthorizations
                .Where(authorization => authorizationIds.Contains(authorization.Id))
                .ToListAsync(cancellationToken);

        if (refreshSessions.Count != refreshIds.Length
            || tokens.Count != tokenIds.Length
            || authorizations.Count != authorizationIds.Length)
        {
            throw new CloudWriteConflictException();
        }

        foreach (var sessionTarget in target.RefreshSessions)
        {
            var session = refreshSessions.Single(candidate => candidate.Id == sessionTarget.Id);
            if (MatchesRefreshTarget(session, target))
            {
                continue;
            }

            if (session.RowVersion != sessionTarget.RowVersion
                || session.RevokedAtUtc.HasValue
                || session.SubjectId != target.SubjectId
                || !string.Equals(
                    session.ActorType,
                    IIoTClaimTypes.HumanActor,
                    StringComparison.Ordinal))
            {
                throw new CloudWriteConflictException();
            }

            session.RevokedAtUtc = target.RevokedAtUtc;
            session.RevokedReason = target.Reason;
        }

        foreach (var tokenTarget in target.Tokens)
        {
            var token = tokens.Single(candidate => candidate.Id == tokenTarget.Id);
            if (MatchesGrantTarget(token.Status, token.ConcurrencyToken, tokenTarget))
            {
                continue;
            }

            if (!string.Equals(token.Subject, target.Subject, StringComparison.Ordinal)
                || string.Equals(
                    token.Status,
                    OpenIddictConstants.Statuses.Revoked,
                    StringComparison.Ordinal)
                || !string.Equals(
                    token.ConcurrencyToken,
                    tokenTarget.BaselineConcurrencyToken,
                    StringComparison.Ordinal))
            {
                throw new CloudWriteConflictException();
            }

            token.Status = OpenIddictConstants.Statuses.Revoked;
            token.ConcurrencyToken = tokenTarget.TargetConcurrencyToken;
        }

        foreach (var authorizationTarget in target.Authorizations)
        {
            var authorization = authorizations.Single(
                candidate => candidate.Id == authorizationTarget.Id);
            if (MatchesGrantTarget(
                    authorization.Status,
                    authorization.ConcurrencyToken,
                    authorizationTarget))
            {
                continue;
            }

            if (!string.Equals(
                    authorization.Subject,
                    target.Subject,
                    StringComparison.Ordinal)
                || string.Equals(
                    authorization.Status,
                    OpenIddictConstants.Statuses.Revoked,
                    StringComparison.Ordinal)
                || !string.Equals(
                    authorization.ConcurrencyToken,
                    authorizationTarget.BaselineConcurrencyToken,
                    StringComparison.Ordinal))
            {
                throw new CloudWriteConflictException();
            }

            authorization.Status = OpenIddictConstants.Statuses.Revoked;
            authorization.ConcurrencyToken = authorizationTarget.TargetConcurrencyToken;
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

    private async Task ObserveOutcomeAsync(RevocationTarget target)
    {
        using var observationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await using var strategyContext = _createContext();
            var strategy = strategyContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(
                callbackToken => ObserveOutcomeAttemptAsync(target, callbackToken),
                observationTimeout.Token);
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

    private async Task ObserveOutcomeAttemptAsync(
        RevocationTarget target,
        CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        var refreshIds = target.RefreshSessions.Select(session => session.Id).ToArray();
        var tokenIds = target.Tokens.Select(token => token.Id).ToArray();
        var authorizationIds = target.Authorizations
            .Select(authorization => authorization.Id)
            .ToArray();
        var refreshSessions = refreshIds.Length == 0
            ? []
            : await context.RefreshTokenSessions
                .AsNoTracking()
                .Where(session => refreshIds.Contains(session.Id))
                .ToListAsync(cancellationToken);
        var tokens = tokenIds.Length == 0
            ? []
            : await context.OpenIddictTokens
                .AsNoTracking()
                .Where(token => tokenIds.Contains(token.Id))
                .ToListAsync(cancellationToken);
        var authorizations = authorizationIds.Length == 0
            ? []
            : await context.OpenIddictAuthorizations
                .AsNoTracking()
                .Where(authorization => authorizationIds.Contains(authorization.Id))
                .ToListAsync(cancellationToken);

        if (refreshSessions.Count != refreshIds.Length
            || tokens.Count != tokenIds.Length
            || authorizations.Count != authorizationIds.Length)
        {
            throw new CloudWriteConflictException();
        }

        foreach (var sessionTarget in target.RefreshSessions)
        {
            var session = refreshSessions.Single(candidate => candidate.Id == sessionTarget.Id);
            if (MatchesRefreshTarget(session, target))
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

        foreach (var tokenTarget in target.Tokens)
        {
            var token = tokens.Single(candidate => candidate.Id == tokenTarget.Id);
            ClassifyGrantOutcome(token.Status, token.ConcurrencyToken, tokenTarget);
        }

        foreach (var authorizationTarget in target.Authorizations)
        {
            var authorization = authorizations.Single(
                candidate => candidate.Id == authorizationTarget.Id);
            ClassifyGrantOutcome(
                authorization.Status,
                authorization.ConcurrencyToken,
                authorizationTarget);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static void ClassifyGrantOutcome(
        string? status,
        string? concurrencyToken,
        GrantTarget target)
    {
        if (MatchesGrantTarget(status, concurrencyToken, target))
        {
            return;
        }

        if (!string.Equals(
                status,
                OpenIddictConstants.Statuses.Revoked,
                StringComparison.Ordinal)
            && string.Equals(
                concurrencyToken,
                target.BaselineConcurrencyToken,
                StringComparison.Ordinal))
        {
            throw new CloudWriteCommitUnknownException();
        }

        throw new CloudWriteConflictException();
    }

    private static bool MatchesRefreshTarget(
        RefreshTokenSession session,
        RevocationTarget target)
        => session.SubjectId == target.SubjectId
           && string.Equals(
               session.ActorType,
               IIoTClaimTypes.HumanActor,
               StringComparison.Ordinal)
           && session.RevokedAtUtc == target.RevokedAtUtc
           && string.Equals(session.RevokedReason, target.Reason, StringComparison.Ordinal);

    private static bool MatchesGrantTarget(
        string? status,
        string? concurrencyToken,
        GrantTarget target)
        => string.Equals(
               status,
               OpenIddictConstants.Statuses.Revoked,
               StringComparison.Ordinal)
           && string.Equals(
               concurrencyToken,
               target.TargetConcurrencyToken,
               StringComparison.Ordinal);

    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
    }

    private sealed record RefreshSessionTarget(Guid Id, uint RowVersion);

    private sealed record GrantBaseline(
        Guid Id,
        string? ConcurrencyToken);

    private sealed record GrantTarget(
        Guid Id,
        string? BaselineConcurrencyToken,
        string TargetConcurrencyToken);

    private sealed record RevocationBaseline(
        IReadOnlyList<RefreshSessionTarget> RefreshSessions,
        IReadOnlyList<GrantBaseline> Tokens,
        IReadOnlyList<GrantBaseline> Authorizations);

    private sealed record RevocationTarget(
        Guid SubjectId,
        string Subject,
        string Reason,
        DateTimeOffset RevokedAtUtc,
        IReadOnlyList<RefreshSessionTarget> RefreshSessions,
        IReadOnlyList<GrantTarget> Tokens,
        IReadOnlyList<GrantTarget> Authorizations);
}
