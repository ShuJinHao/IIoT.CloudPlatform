using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.Services.Contracts.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIoT.ServiceLayer.Tests;

public sealed class RefreshTokenSessionLimitTests
{
    [Fact]
    public async Task IssueAsync_ShouldRevokeOldestHumanSessionWhenLimitIsReached()
    {
        await using var dbContext = CreateDbContext();
        var subjectId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var oldestSessionId = Guid.NewGuid();
        var middleSessionId = Guid.NewGuid();
        var newestSessionId = Guid.NewGuid();

        dbContext.RefreshTokenSessions.AddRange(
            CreateSeedSession(oldestSessionId, IIoTClaimTypes.HumanActor, subjectId, "seed-oldest", now.AddMinutes(-3)),
            CreateSeedSession(middleSessionId, IIoTClaimTypes.HumanActor, subjectId, "seed-middle", now.AddMinutes(-2)),
            CreateSeedSession(newestSessionId, IIoTClaimTypes.HumanActor, subjectId, "seed-newest", now.AddMinutes(-1)));
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, humanMaxActiveSessions: 3);

        await service.IssueAsync(IIoTClaimTypes.HumanActor, subjectId);

        var sessions = await dbContext.RefreshTokenSessions
            .Where(x => x.ActorType == IIoTClaimTypes.HumanActor && x.SubjectId == subjectId)
            .ToListAsync();
        var oldestSession = sessions.Single(x => x.Id == oldestSessionId);

        Assert.Equal(4, sessions.Count);
        Assert.Equal(3, CountActiveSessions(sessions));
        Assert.NotNull(oldestSession.RevokedAtUtc);
        Assert.Equal("session-limit", oldestSession.RevokedReason);
        Assert.Null(sessions.Single(x => x.Id == middleSessionId).RevokedAtUtc);
        Assert.Null(sessions.Single(x => x.Id == newestSessionId).RevokedAtUtc);
    }

    [Fact]
    public async Task IssueAsync_ShouldNotLimitEdgeDeviceSessionsWithHumanLimitConfigured()
    {
        await using var dbContext = CreateDbContext();
        var subjectId = Guid.NewGuid();
        var service = CreateService(dbContext, humanMaxActiveSessions: 1);

        await service.IssueAsync(IIoTClaimTypes.EdgeDeviceActor, subjectId);
        await service.IssueAsync(IIoTClaimTypes.EdgeDeviceActor, subjectId);

        var sessions = await dbContext.RefreshTokenSessions
            .Where(x => x.ActorType == IIoTClaimTypes.EdgeDeviceActor && x.SubjectId == subjectId)
            .ToListAsync();

        Assert.Equal(2, sessions.Count);
        Assert.Equal(2, CountActiveSessions(sessions));
        Assert.All(sessions, session => Assert.Null(session.RevokedReason));
    }

    [Fact]
    public async Task RotateAsync_ShouldNotApplyHumanLoginSessionLimit()
    {
        await using var dbContext = CreateDbContext();
        var subjectId = Guid.NewGuid();
        var unlimitedService = CreateService(dbContext, humanMaxActiveSessions: 0);

        var firstSession = await unlimitedService.IssueAsync(IIoTClaimTypes.HumanActor, subjectId);
        await unlimitedService.IssueAsync(IIoTClaimTypes.HumanActor, subjectId);

        var limitedService = CreateService(dbContext, humanMaxActiveSessions: 1);

        var rotationResult = await limitedService.RotateAsync(IIoTClaimTypes.HumanActor, firstSession.Token);

        Assert.True(rotationResult.IsSuccess);

        var sessions = await dbContext.RefreshTokenSessions
            .Where(x => x.ActorType == IIoTClaimTypes.HumanActor && x.SubjectId == subjectId)
            .ToListAsync();

        Assert.Equal(3, sessions.Count);
        Assert.Equal(2, CountActiveSessions(sessions));
        Assert.Single(sessions, x => x.RevokedReason == "rotated");
        Assert.DoesNotContain(sessions, x => x.RevokedReason == "session-limit");
    }

    [Fact]
    public void RefreshTokenOptions_ShouldRejectNegativeHumanSessionLimit()
    {
        var options = new RefreshTokenOptions
        {
            HumanMaxActiveSessions = -1
        };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains("active session limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IIoTDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<IIoTDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new IIoTDbContext(options);
    }

    private static EfRefreshTokenService CreateService(
        IIoTDbContext dbContext,
        int humanMaxActiveSessions)
    {
        return new EfRefreshTokenService(
            dbContext,
            Options.Create(new RefreshTokenOptions
            {
                HumanMaxActiveSessions = humanMaxActiveSessions
            }));
    }

    private static RefreshTokenSession CreateSeedSession(
        Guid id,
        string actorType,
        Guid subjectId,
        string tokenHash,
        DateTimeOffset createdAtUtc)
    {
        return new RefreshTokenSession
        {
            Id = id,
            ActorType = actorType,
            SubjectId = subjectId,
            TokenHash = tokenHash,
            CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1)
        };
    }

    private static int CountActiveSessions(IEnumerable<RefreshTokenSession> sessions)
    {
        var now = DateTimeOffset.UtcNow;
        return sessions.Count(x => !x.RevokedAtUtc.HasValue && x.ExpiresAtUtc > now);
    }
}
