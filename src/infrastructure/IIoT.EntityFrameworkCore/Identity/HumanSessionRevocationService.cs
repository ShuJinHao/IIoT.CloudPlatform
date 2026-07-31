using IIoT.Services.Contracts.Identity;
using IIoT.EntityFrameworkCore.Persistence;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace IIoT.EntityFrameworkCore.Identity;

public sealed class HumanSessionRevocationService(IIoTDbContext dbContext)
    : IHumanSessionRevocationService
{
    public async Task RevokeAllAsync(
        Guid subjectId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var subject = subjectId.ToString();
        await RefreshTokenSubjectTransactionLock.AcquireForOidcRevocationAsync(
            dbContext,
            subjectId,
            cancellationToken);

        var refreshSessions = await dbContext.RefreshTokenSessions
            .Where(session =>
                session.ActorType == IIoTClaimTypes.HumanActor &&
                session.SubjectId == subjectId &&
                !session.RevokedAtUtc.HasValue)
            .ToListAsync(cancellationToken);
        var oidcTokens = await dbContext.OpenIddictTokens
            .Where(token =>
                token.Subject == subject &&
                token.Status != OpenIddictConstants.Statuses.Revoked)
            .ToListAsync(cancellationToken);
        var oidcAuthorizations = await dbContext.OpenIddictAuthorizations
            .Where(authorization =>
                authorization.Subject == subject &&
                authorization.Status != OpenIddictConstants.Statuses.Revoked)
            .ToListAsync(cancellationToken);

        foreach (var session in refreshSessions)
        {
            session.RevokedAtUtc = now;
            session.RevokedReason = reason;
        }

        foreach (var token in oidcTokens)
        {
            token.Status = OpenIddictConstants.Statuses.Revoked;
            token.ConcurrencyToken = Guid.NewGuid().ToString("N");
        }

        foreach (var authorization in oidcAuthorizations)
        {
            authorization.Status = OpenIddictConstants.Statuses.Revoked;
            authorization.ConcurrencyToken = Guid.NewGuid().ToString("N");
        }

        if (refreshSessions.Count > 0 || oidcTokens.Count > 0 || oidcAuthorizations.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
