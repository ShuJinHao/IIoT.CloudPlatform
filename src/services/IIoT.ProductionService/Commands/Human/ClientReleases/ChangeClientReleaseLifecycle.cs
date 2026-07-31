using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Persistence;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Persistence;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.ClientReleases;

[AuthorizeRequirement(ClientReleasePermissions.Manage)]
[DistributedLock(
    ClientReleasePublishLock.Resource,
    TimeoutSeconds = ClientReleasePublishLock.AcquireTimeoutSeconds)]
public sealed record ArchiveClientReleaseCommand(Guid ReleaseId)
    : IHumanCommand<Result>;

public sealed class ArchiveClientReleaseHandler(
    IRepository<ClientReleaseComponent> componentRepository,
    IUnitOfWork unitOfWork,
    IClientReleaseWriteObservationReader observationReader)
    : ICommandHandler<ArchiveClientReleaseCommand, Result>
{
    public async Task<Result> Handle(ArchiveClientReleaseCommand request, CancellationToken cancellationToken)
    {
        var baselineObservation =
            await CloudWriteCommitRecovery
                .TryObserveOptionalAttemptAsync(
            token => observationReader.ObserveVersionAsync(
                request.ReleaseId,
                token),
            cancellationToken);
        if (baselineObservation is null)
        {
            throw new CloudWriteCommitUnknownException();
        }

        var baseline = baselineObservation.Value;
        if (baseline is null)
        {
            var exists = await componentRepository.AnyAsync(
                component => component.Versions.Any(
                    version => version.Id == request.ReleaseId),
                cancellationToken);
            if (!exists)
            {
                return Result.NotFound("发布版本不存在。");
            }

            throw new CloudWriteCommitUnknownException();
        }

        if (baseline.Status == ClientReleaseStatus.Archived
            && baseline.DeletedAtUtc is null
            && baseline.DeletionReason is null
            && baseline.DeletionFailure is null)
        {
            return Result.Success();
        }

        if (baseline.Status is ClientReleaseStatus.DeleteRequested
            or ClientReleaseStatus.Deleted
            or ClientReleaseStatus.DeleteFailed)
        {
            return Result.Invalid("删除状态的发布版本不能归档或复活。");
        }

        var changedAtUtc =
            ClientReleaseWriteCommitRecovery.NormalizeUtc(DateTime.UtcNow);
        try
        {
            await unitOfWork.ExecuteResilientAsync(
                ExecuteAttemptAsync,
                cancellationToken);
            return Result.Success();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CloudWriteException)
        {
            throw;
        }
        catch
        {
            var currentObservation =
                await CloudWriteCommitRecovery
                    .TryObserveOptionalCommitAsync(
                    token => observationReader.ObserveVersionAsync(
                        request.ReleaseId,
                        token));
            if (currentObservation is null)
            {
                throw new CloudWriteCommitUnknownException();
            }

            var current = currentObservation.Value;
            if (current is null || current == baseline)
            {
                throw new CloudWriteCommitUnknownException();
            }

            if (ClientReleaseWriteCommitRecovery.MatchesVersionTarget(
                    current,
                    baseline,
                    ClientReleaseStatus.Archived))
            {
                return Result.Success();
            }

            throw new CloudWriteConflictException();
        }

        async Task<bool> ExecuteAttemptAsync(
            CancellationToken callbackCancellationToken)
        {
            var currentObservation =
                await CloudWriteCommitRecovery
                    .TryObserveOptionalAttemptAsync(
                    token => observationReader.ObserveVersionAsync(
                        request.ReleaseId,
                        token),
                    callbackCancellationToken)
                ?? throw new CloudWriteCommitUnknownException();
            var current = currentObservation.Value
                          ?? throw new CloudWriteCommitUnknownException();
            if (ClientReleaseWriteCommitRecovery.MatchesVersionTarget(
                    current,
                    baseline,
                    ClientReleaseStatus.Archived))
            {
                return true;
            }

            if (current != baseline)
            {
                throw new CloudWriteConflictException();
            }

            var component =
                await componentRepository.GetSingleOrDefaultAsync(
                    new ClientReleaseComponentByVersionIdSpec(
                        request.ReleaseId),
                    callbackCancellationToken)
                ?? throw new CloudWriteConflictException();
            var version = component.FindVersion(request.ReleaseId)
                          ?? throw new CloudWriteConflictException();
            if (ClientReleaseWriteStateFingerprint.ForVersion(
                    component,
                    version) != baseline)
            {
                throw new CloudWriteConflictException();
            }

            component.ChangeVersionStatus(
                request.ReleaseId,
                ClientReleaseStatus.Archived,
                changedAtUtc);
            await componentRepository.SaveChangesAsync(
                callbackCancellationToken);
            return true;
        }
    }
}
