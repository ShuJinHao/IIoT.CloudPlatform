using System.Buffers.Binary;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.Persistence;

internal static class RefreshTokenSubjectTransactionLock
{
    private const long LockNamespace = 0x5254465300000000;
    private const long OidcTokenExchangeLockKey = 0x4F49444349535355;

    public static async Task AcquireAsync(
        IIoTDbContext dbContext,
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        EnsureTransaction(dbContext);
        if (!dbContext.Database.IsNpgsql()) return;
        await AcquireSubjectCoreAsync(dbContext, subjectId, cancellationToken);
    }

    public static async Task AcquireOidcTokenExchangeAsync(
        IIoTDbContext dbContext,
        CancellationToken cancellationToken)
    {
        EnsureTransaction(dbContext);
        if (!dbContext.Database.IsNpgsql()) return;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({OidcTokenExchangeLockKey});",
            cancellationToken);
    }

    public static async Task AcquireForOidcRevocationAsync(
        IIoTDbContext dbContext,
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        EnsureTransaction(dbContext);
        if (!dbContext.Database.IsNpgsql()) return;
        await AcquireOidcTokenExchangeAsync(dbContext, cancellationToken);
        await AcquireSubjectCoreAsync(dbContext, subjectId, cancellationToken);
    }

    private static async Task AcquireSubjectCoreAsync(
        IIoTDbContext dbContext,
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        var bytes = subjectId.ToByteArray();
        var subjectKey = BinaryPrimitives.ReadInt64BigEndian(
            bytes.AsSpan(0, sizeof(long))) ^ LockNamespace;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({subjectKey});",
            cancellationToken);
    }

    private static void EnsureTransaction(IIoTDbContext dbContext)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Human-session synchronization requires an active transaction.");
        }
    }
}
