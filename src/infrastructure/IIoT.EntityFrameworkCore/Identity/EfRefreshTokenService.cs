using System.Security.Cryptography;
using System.Text;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Result;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IIoT.EntityFrameworkCore.Identity;

public sealed class EfRefreshTokenService(
    IIoTDbContext dbContext,
    IOptions<RefreshTokenOptions> refreshTokenOptions) : IRefreshTokenService
{
    private readonly RefreshTokenOptions _options = refreshTokenOptions.Value;

    public async Task<RefreshTokenEnvelope> IssueAsync(
        string actorType,
        Guid subjectId,
        CancellationToken cancellationToken = default)
    {
        var token = GenerateToken();
        var session = CreateSession(actorType, subjectId, token);

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

        var replacementToken = GenerateToken();
        var replacementSession = CreateSession(actorType, existing.SubjectId, replacementToken);

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
            new RefreshTokenEnvelope(replacementToken, replacementSession.ExpiresAtUtc)));
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

    private RefreshTokenSession CreateSession(
        string actorType,
        Guid subjectId,
        string token)
    {
        var now = DateTimeOffset.UtcNow;
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

    private static string GenerateToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(64));
    }

    private static string ComputeTokenHash(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
