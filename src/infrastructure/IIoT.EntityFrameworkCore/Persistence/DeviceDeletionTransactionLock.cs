using System.Buffers.Binary;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.Persistence;

internal static class DeviceDeletionTransactionLock
{
    private const long LockNamespace = 0x4445564300000000;

    public static async Task AcquireAsync(
        IIoTDbContext dbContext,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Device deletion synchronization requires an active transaction.");
        }

        if (!dbContext.Database.IsNpgsql())
        {
            return;
        }

        var bytes = deviceId.ToByteArray();
        var deviceKey = BinaryPrimitives.ReadInt64BigEndian(
            bytes.AsSpan(0, sizeof(long))) ^ LockNamespace;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({deviceKey});",
            cancellationToken);
    }
}
