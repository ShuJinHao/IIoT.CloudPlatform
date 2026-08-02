using IIoT.EntityFrameworkCore.Outbox;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IIoT.EntityFrameworkCore.Uploads;

public sealed class EfUploadReceiveRegistry(IIoTDbContext dbContext)
    : IUploadReceiveRegistry
{
    private readonly Func<IIoTDbContext> _createContext = dbContext.CreateFreshContext;

    public async Task<UploadReceiveRegistrationResult> RegisterAndEnqueueAsync(
        Guid deviceId,
        string messageType,
        string? requestId,
        string deduplicationKey,
        IIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default,
        string? contentFingerprint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentException.ThrowIfNullOrWhiteSpace(deduplicationKey);
        ArgumentNullException.ThrowIfNull(integrationEvent);
        cancellationToken.ThrowIfCancellationRequested();

        requestId = NormalizeRequestId(requestId);
        var registrationId = Guid.NewGuid();
        var outboxMessageId = Guid.NewGuid();
        var receivedAtUtc = OutboxMessage.NormalizePostgresTimestamp(DateTimeOffset.UtcNow);
        var targetOutboxMessage = OutboxMessage.FromIntegrationEvent(
            integrationEvent,
            outboxMessageId,
            receivedAtUtc);
        await using var strategyContext = _createContext();
        var strategy = strategyContext.Database.CreateExecutionStrategy();

        UploadReceiveRegistrationResult result;
        try
        {
            result = await strategy.ExecuteAsync(
                async callbackToken =>
                {
                    return await RegisterAttemptAsync(
                        registrationId,
                        deviceId,
                        messageType,
                        requestId,
                        deduplicationKey,
                        contentFingerprint,
                        targetOutboxMessage,
                        receivedAtUtc,
                        callbackToken);
                },
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
            result = await ObserveCommitOutcomeAsync(
                registrationId,
                deviceId,
                messageType,
                requestId,
                deduplicationKey,
                contentFingerprint,
                targetOutboxMessage,
                receivedAtUtc);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private async Task<UploadReceiveRegistrationResult> RegisterAttemptAsync(
        Guid registrationId,
        Guid deviceId,
        string messageType,
        string? requestId,
        string deduplicationKey,
        string? contentFingerprint,
        OutboxMessage targetOutboxMessage,
        DateTimeOffset receivedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        var existing = await FindExistingAsync(
            context,
            deviceId,
            messageType,
            deduplicationKey,
            cancellationToken);
        if (existing is not null)
        {
            return await ClassifyExistingAsync(
                context,
                existing,
                registrationId,
                requestId,
                contentFingerprint,
                targetOutboxMessage,
                receivedAtUtc,
                recordDuplicateObservation: true,
                cancellationToken);
        }

        var registration = UploadReceiveRegistration.Create(
            registrationId,
            deviceId,
            messageType,
            requestId,
            deduplicationKey,
            targetOutboxMessage.Id,
            receivedAtUtc,
            contentFingerprint);

        context.UploadReceiveRegistrations.Add(registration);
        context.OutboxMessages.Add(targetOutboxMessage);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return UploadReceiveRegistrationResult.Registered(targetOutboxMessage.Id);
        }
        catch (DbUpdateException exception) when (IsDeduplicationConflict(exception))
        {
            context.ChangeTracker.Clear();
            existing = await FindExistingAsync(
                context,
                deviceId,
                messageType,
                deduplicationKey,
                cancellationToken);
            return existing is null
                ? throw new CloudWriteCommitUnknownException()
                : await ClassifyExistingAsync(
                    context,
                    existing,
                    registrationId,
                    requestId,
                    contentFingerprint,
                    targetOutboxMessage,
                    receivedAtUtc,
                    recordDuplicateObservation: true,
                    cancellationToken);
        }
    }

    private static async Task<UploadReceiveRegistration?> FindExistingAsync(
        IIoTDbContext context,
        Guid deviceId,
        string messageType,
        string deduplicationKey,
        CancellationToken cancellationToken)
    {
        return await context.UploadReceiveRegistrations
            .SingleOrDefaultAsync(
                x => x.DeviceId == deviceId
                     && x.MessageType == messageType
                     && x.DeduplicationKey == deduplicationKey,
                cancellationToken);
    }

    private async Task<UploadReceiveRegistrationResult> ClassifyExistingAsync(
        IIoTDbContext context,
        UploadReceiveRegistration registration,
        Guid targetRegistrationId,
        string? targetRequestId,
        string? targetContentFingerprint,
        OutboxMessage targetOutboxMessage,
        DateTimeOffset seenAtUtc,
        bool recordDuplicateObservation,
        CancellationToken cancellationToken)
    {
        if (targetContentFingerprint is not null
            && !string.Equals(
                registration.ContentFingerprint,
                targetContentFingerprint,
                StringComparison.Ordinal))
        {
            throw new CloudWriteConflictException();
        }

        if (registration.Id == targetRegistrationId)
        {
            if (registration.OutboxMessageId != targetOutboxMessage.Id
                || !string.Equals(
                    registration.RequestId,
                    targetRequestId,
                    StringComparison.Ordinal)
                || registration.ReceivedAtUtc != seenAtUtc)
            {
                throw new CloudWriteConflictException();
            }

            var persistedOutbox = await context.OutboxMessages
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    message => message.Id == targetOutboxMessage.Id,
                    cancellationToken);
            if (persistedOutbox is null)
            {
                throw new CloudWriteCommitUnknownException();
            }

            if (!MatchesTarget(persistedOutbox, targetOutboxMessage))
            {
                throw new CloudWriteConflictException();
            }

            return UploadReceiveRegistrationResult.Registered(targetOutboxMessage.Id);
        }

        return recordDuplicateObservation
            ? await RecordDuplicateObservationAsync(
                context,
                registration,
                targetRegistrationId,
                seenAtUtc,
                cancellationToken)
            : await ObserveDuplicateOutcomeAsync(
                context,
                registration,
                targetRegistrationId,
                seenAtUtc,
                cancellationToken);
    }

    private static async Task<UploadReceiveRegistrationResult>
        RecordDuplicateObservationAsync(
            IIoTDbContext context,
            UploadReceiveRegistration registration,
            Guid observationId,
            DateTimeOffset seenAtUtc,
            CancellationToken cancellationToken)
    {
        await using var transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        var existingObservation = await context.UploadReceiveObservations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                observation => observation.Id == observationId,
                cancellationToken);
        if (existingObservation is not null)
        {
            EnsureObservationTarget(
                existingObservation,
                registration.Id,
                seenAtUtc);
            await transaction.CommitAsync(cancellationToken);
            return UploadReceiveRegistrationResult.Duplicate(
                registration.OutboxMessageId);
        }

        context.UploadReceiveObservations.Add(
            UploadReceiveObservation.Create(
                observationId,
                registration.Id,
                seenAtUtc));
        await context.SaveChangesAsync(cancellationToken);

        if (context.Database.IsNpgsql())
        {
            var updated = await context.UploadReceiveRegistrations
                .Where(candidate => candidate.Id == registration.Id)
                .ExecuteUpdateAsync(
                    updates => updates
                        .SetProperty(
                            candidate => candidate.SeenCount,
                            candidate => candidate.SeenCount + 1)
                        .SetProperty(
                            candidate => candidate.LastSeenAtUtc,
                            candidate =>
                                candidate.LastSeenAtUtc < seenAtUtc
                                    ? seenAtUtc
                                    : candidate.LastSeenAtUtc),
                    cancellationToken);
            if (updated != 1)
            {
                throw new CloudWriteConflictException();
            }
        }
        else
        {
            registration.MarkSeen(seenAtUtc);
            await context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return UploadReceiveRegistrationResult.Duplicate(
            registration.OutboxMessageId);
    }

    private static async Task<UploadReceiveRegistrationResult>
        ObserveDuplicateOutcomeAsync(
            IIoTDbContext context,
            UploadReceiveRegistration registration,
            Guid observationId,
            DateTimeOffset seenAtUtc,
            CancellationToken cancellationToken)
    {
        var observation = await context.UploadReceiveObservations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == observationId,
                cancellationToken);
        if (observation is null)
        {
            throw new CloudWriteCommitUnknownException();
        }

        EnsureObservationTarget(
            observation,
            registration.Id,
            seenAtUtc);
        return UploadReceiveRegistrationResult.Duplicate(
            registration.OutboxMessageId);
    }

    private static void EnsureObservationTarget(
        UploadReceiveObservation observation,
        Guid registrationId,
        DateTimeOffset seenAtUtc)
    {
        if (observation.RegistrationId != registrationId
            || observation.SeenAtUtc != seenAtUtc)
        {
            throw new CloudWriteConflictException();
        }
    }

    private async Task<UploadReceiveRegistrationResult> ObserveCommitOutcomeAsync(
        Guid registrationId,
        Guid deviceId,
        string messageType,
        string? requestId,
        string deduplicationKey,
        string? contentFingerprint,
        OutboxMessage targetOutboxMessage,
        DateTimeOffset receivedAtUtc)
    {
        using var observationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await using var context = _createContext();
            var existing = await FindExistingAsync(
                context,
                deviceId,
                messageType,
                deduplicationKey,
                observationTimeout.Token);
            if (existing is null)
            {
                throw new CloudWriteCommitUnknownException();
            }

            return await ClassifyExistingAsync(
                context,
                existing,
                registrationId,
                requestId,
                contentFingerprint,
                targetOutboxMessage,
                receivedAtUtc,
                recordDuplicateObservation: false,
                observationTimeout.Token);
        }
        catch (CloudWriteConflictException)
        {
            throw;
        }
        catch (CloudWriteCommitUnknownException)
        {
            throw;
        }
        catch
        {
            throw new CloudWriteCommitUnknownException();
        }
    }

    private static bool MatchesTarget(OutboxMessage persisted, OutboxMessage target)
        => persisted.Id == target.Id
           && persisted.MessageKind == target.MessageKind
           && string.Equals(persisted.EventType, target.EventType, StringComparison.Ordinal)
           && OutboxMessage.JsonPayloadEquals(persisted.Payload, target.Payload)
           && persisted.OccurredAtUtc == target.OccurredAtUtc;

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
