using IIoT.EntityFrameworkCore.Outbox;
using IIoT.Services.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IIoT.EntityFrameworkCore.Uploads;

public sealed class EfUploadReceiveRegistry(IIoTDbContext dbContext)
    : IUploadReceiveRegistry
{
    public async Task<UploadReceiveRegistrationResult> RegisterAndEnqueueAsync(
        Guid deviceId,
        string messageType,
        string? requestId,
        string deduplicationKey,
        IIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentException.ThrowIfNullOrWhiteSpace(deduplicationKey);
        ArgumentNullException.ThrowIfNull(integrationEvent);

        requestId = NormalizeRequestId(requestId);

        var existing = await FindExistingAsync(
            deviceId,
            messageType,
            deduplicationKey,
            cancellationToken);
        if (existing is not null)
        {
            return await MarkDuplicateAsync(existing, cancellationToken);
        }

        var outboxMessage = OutboxMessage.FromIntegrationEvent(integrationEvent);
        var registration = UploadReceiveRegistration.Create(
            deviceId,
            messageType,
            requestId,
            deduplicationKey,
            outboxMessage.Id);

        dbContext.UploadReceiveRegistrations.Add(registration);
        dbContext.OutboxMessages.Add(outboxMessage);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return UploadReceiveRegistrationResult.Registered(outboxMessage.Id);
        }
        catch (DbUpdateException exception) when (IsDeduplicationConflict(exception))
        {
            dbContext.ChangeTracker.Clear();
            existing = await FindExistingAsync(
                deviceId,
                messageType,
                deduplicationKey,
                cancellationToken);

            return existing is null
                ? UploadReceiveRegistrationResult.Duplicate(null)
                : await MarkDuplicateAsync(existing, cancellationToken);
        }
    }

    private async Task<UploadReceiveRegistration?> FindExistingAsync(
        Guid deviceId,
        string messageType,
        string deduplicationKey,
        CancellationToken cancellationToken)
    {
        return await dbContext.UploadReceiveRegistrations
            .SingleOrDefaultAsync(
                x => x.DeviceId == deviceId
                     && x.MessageType == messageType
                     && x.DeduplicationKey == deduplicationKey,
                cancellationToken);
    }

    private async Task<UploadReceiveRegistrationResult> MarkDuplicateAsync(
        UploadReceiveRegistration registration,
        CancellationToken cancellationToken)
    {
        registration.MarkSeen();
        await dbContext.SaveChangesAsync(cancellationToken);
        return UploadReceiveRegistrationResult.Duplicate(registration.OutboxMessageId);
    }

    private static string? NormalizeRequestId(string? requestId)
    {
        requestId = requestId?.Trim();
        return string.IsNullOrWhiteSpace(requestId) ? null : requestId;
    }

    private static bool IsDeduplicationConflict(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
               && string.Equals(
                   postgresException.SqlState,
                   PostgresErrorCodes.UniqueViolation,
                   StringComparison.Ordinal)
               && string.Equals(
                   postgresException.ConstraintName,
                   UploadReceiveRegistrationConfiguration.UniqueDeduplicationIndexName,
                   StringComparison.Ordinal);
    }
}
