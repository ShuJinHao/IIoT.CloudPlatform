using System.Security.Cryptography;
using System.Text;
using IIoT.EntityFrameworkCore.Persistence;
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
    private readonly RefreshTokenOptions _options = refreshTokenOptions.Value;

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
            cancellationToken);
    }

    public async Task<RefreshTokenEnvelope> IssueAsync(
        string actorType,
        Guid subjectId,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(actorType, IIoTClaimTypes.HumanActor, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Human refresh tokens must be issued with an identity status version.");
        }

        if (string.Equals(
                actorType,
                IIoTClaimTypes.EdgeDeviceActor,
                StringComparison.Ordinal))
        {
            return await IssueEdgeDeviceAsync(subjectId, cancellationToken);
        }

        return await IssueCoreAsync(actorType, subjectId, null, cancellationToken);
    }

    private async Task<RefreshTokenEnvelope> IssueEdgeDeviceAsync(
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var token = GenerateToken(null);
        var session = CreateSession(
            IIoTClaimTypes.EdgeDeviceActor,
            subjectId,
            token,
            now);
        var envelope = new RefreshTokenEnvelope(token, session.ExpiresAtUtc);
        var strategy = dbContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(
            ExecuteTransactionAsync,
            cancellationToken);

        async Task<RefreshTokenEnvelope> ExecuteTransactionAsync(
            CancellationToken transactionCancellationToken)
        {
            try
            {
                await using var transaction =
                    await dbContext.Database.BeginTransactionAsync(
                        transactionCancellationToken);
                await DeviceDeletionTransactionLock.AcquireAsync(
                    dbContext,
                    subjectId,
                    transactionCancellationToken);

                var deviceExists = await dbContext.Devices
                    .AsNoTracking()
                    .AnyAsync(
                        device => device.Id == subjectId,
                        transactionCancellationToken);
                if (!deviceExists)
                {
                    throw new InvalidOperationException(
                        "Edge device refresh session cannot be issued because the device no longer exists.");
                }

                var committedSession = await dbContext.RefreshTokenSessions
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        candidate => candidate.Id == session.Id,
                        transactionCancellationToken);
                if (committedSession is not null)
                {
                    if (!string.Equals(
                            committedSession.ActorType,
                            session.ActorType,
                            StringComparison.Ordinal)
                        || committedSession.SubjectId != session.SubjectId
                        || !string.Equals(
                            committedSession.TokenHash,
                            session.TokenHash,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Edge device refresh session replay state is inconsistent.");
                    }

                    await transaction.CommitAsync(
                        transactionCancellationToken);
                    return envelope;
                }

                dbContext.RefreshTokenSessions.Add(session);
                await dbContext.SaveChangesAsync(transactionCancellationToken);
                await transaction.CommitAsync(transactionCancellationToken);
                return envelope;
            }
            catch
            {
                dbContext.ChangeTracker.Clear();
                throw;
            }
        }
    }

    private async Task<RefreshTokenEnvelope> IssueCoreAsync(
        string actorType,
        Guid subjectId,
        string? identityStatusVersion,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var token = GenerateToken(identityStatusVersion);
        var session = CreateSession(actorType, subjectId, token, now);

        await RevokeOverflowHumanSessionsAsync(actorType, subjectId, now, cancellationToken);
        dbContext.RefreshTokenSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RefreshTokenEnvelope(token, session.ExpiresAtUtc);
    }

    public async Task<Result<RefreshTokenRotationResult>> RotateAsync(
        string actorType,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = ComputeTokenHash(refreshToken);
        var now = DateTimeOffset.UtcNow;

        var existing = await dbContext.RefreshTokenSessions
            .SingleOrDefaultAsync(
                x => x.ActorType == actorType && x.TokenHash == tokenHash,
                cancellationToken);

        if (existing is null || existing.RevokedAtUtc.HasValue || existing.ExpiresAtUtc <= now)
        {
            return Result.Unauthorized("Refresh token is invalid or expired.");
        }

        var identityStatusVersion = string.Equals(
            actorType,
            IIoTClaimTypes.HumanActor,
            StringComparison.Ordinal)
            ? TryReadHumanIdentityStatusVersion(refreshToken)
            : null;
        if (string.Equals(actorType, IIoTClaimTypes.HumanActor, StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(identityStatusVersion))
        {
            existing.RevokedAtUtc = now;
            existing.RevokedReason = "status-version-missing";
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Unauthorized("Refresh token is invalid or expired.");
        }

        var replacementToken = GenerateToken(identityStatusVersion);
        var replacementSession = CreateSession(actorType, existing.SubjectId, replacementToken, now);

        existing.RevokedAtUtc = now;
        existing.RevokedReason = "rotated";
        existing.ReplacedByTokenId = replacementSession.Id;

        dbContext.RefreshTokenSessions.Add(replacementSession);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Unauthorized("Refresh token is invalid or expired.");
        }

        return Result.Success(new RefreshTokenRotationResult(
            actorType,
            existing.SubjectId,
            new RefreshTokenEnvelope(replacementToken, replacementSession.ExpiresAtUtc),
            identityStatusVersion));
    }

    public async Task RevokeSubjectTokensAsync(
        string actorType,
        Guid subjectId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var sessions = await dbContext.RefreshTokenSessions
            .Where(x =>
                x.ActorType == actorType &&
                x.SubjectId == subjectId &&
                !x.RevokedAtUtc.HasValue &&
                x.ExpiresAtUtc > now)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.RevokedAtUtc = now;
            session.RevokedReason = reason;
        }

        if (sessions.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task RevokeOverflowHumanSessionsAsync(
        string actorType,
        Guid subjectId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(actorType, IIoTClaimTypes.HumanActor, StringComparison.Ordinal) ||
            _options.HumanMaxActiveSessions <= 0)
        {
            return;
        }

        var activeQuery = dbContext.RefreshTokenSessions
            .Where(x =>
                x.ActorType == actorType &&
                x.SubjectId == subjectId &&
                !x.RevokedAtUtc.HasValue &&
                x.ExpiresAtUtc > now);

        var overflowCount = await activeQuery.CountAsync(cancellationToken) - _options.HumanMaxActiveSessions + 1;
        if (overflowCount <= 0)
        {
            return;
        }

        var sessionsToRevoke = await activeQuery
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .Take(overflowCount)
            .ToListAsync(cancellationToken);

        foreach (var session in sessionsToRevoke)
        {
            session.RevokedAtUtc = now;
            session.RevokedReason = SessionLimitRevokedReason;
        }
    }

    private RefreshTokenSession CreateSession(
        string actorType,
        Guid subjectId,
        string token,
        DateTimeOffset now)
    {
        return new RefreshTokenSession
        {
            Id = Guid.NewGuid(),
            ActorType = actorType,
            SubjectId = subjectId,
            TokenHash = ComputeTokenHash(token),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(ResolveTtlDays(actorType))
        };
    }

    private int ResolveTtlDays(string actorType)
    {
        return string.Equals(actorType, IIoTClaimTypes.EdgeDeviceActor, StringComparison.Ordinal)
            ? _options.EdgeBootstrapTtlDays
            : _options.HumanTtlDays;
    }

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
        if (segments.Length != 3 ||
            !string.Equals(segments[0], HumanTokenVersionPrefix, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(segments[1]) ||
            string.IsNullOrWhiteSpace(segments[2]))
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
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
