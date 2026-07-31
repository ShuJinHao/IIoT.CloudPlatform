using System.Buffers.Binary;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.Persistence;

internal static class RefreshTokenSubjectTransactionLock
{
    private const long LockNamespace = 0x5254465300000000;

    public static async Task AcquireAsync(
        IIoTDbContext dbContext,
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Refresh-token subject synchronization requires an active transaction.");
        }

        if (!dbContext.Database.IsNpgsql())
        {
            return;
        }

        var bytes = subjectId.ToByteArray();
        var subjectKey = BinaryPrimitives.ReadInt64BigEndian(
            bytes.AsSpan(0, sizeof(long))) ^ LockNamespace;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({subjectKey});",
            cancellationToken);
    }
}
