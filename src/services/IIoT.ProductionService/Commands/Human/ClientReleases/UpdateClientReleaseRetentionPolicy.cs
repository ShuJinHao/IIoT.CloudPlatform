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
public sealed record UpdateClientReleaseRetentionPolicyCommand(int MaxVersionsPerComponent)
    : IHumanCommand<Result<ClientReleaseRetentionPolicyDto>>;

public sealed class UpdateClientReleaseRetentionPolicyHandler(
    IRepository<ClientReleaseRetentionPolicy> policyRepository,
    IReadRepository<ClientReleaseComponent> componentRepository,
    IClientReleaseRetentionService retentionService,
    IUnitOfWork unitOfWork,
    IClientReleaseWriteObservationReader observationReader)
    : ICommandHandler<UpdateClientReleaseRetentionPolicyCommand, Result<ClientReleaseRetentionPolicyDto>>
{
    public async Task<Result<ClientReleaseRetentionPolicyDto>> Handle(
        UpdateClientReleaseRetentionPolicyCommand request,
        CancellationToken cancellationToken)
    {
        if (request.MaxVersionsPerComponent is < 1 or > 20)
        {
            return Result.Invalid("每个组件保留版本数必须在 1 到 20 之间。");
        }

        var baselineObservation =
            await CloudWriteCommitRecovery
                .TryObserveOptionalAttemptAsync(
                    observationReader.ObserveRetentionPolicyAsync,
                    cancellationToken)
            ?? throw new CloudWriteCommitUnknownException();
        var baseline = baselineObservation.Value;
        if (baseline is null
            && await policyRepository.AnyAsync(
                policy =>
                    policy.Id == ClientReleaseRetentionPolicy.SingletonId,
                cancellationToken))
        {
            throw new CloudWriteCommitUnknownException();
        }

        var updatedAtUtc =
            ClientReleaseWriteCommitRecovery.NormalizeUtc(DateTime.UtcNow);
        var policyPersisted = false;
        try
        {
            await unitOfWork.ExecuteResilientAsync(
                ExecuteAttemptAsync,
                cancellationToken);
            policyPersisted = true;
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
                        observationReader
                            .ObserveRetentionPolicyAsync)
                ?? throw new CloudWriteCommitUnknownException();
            var current = currentObservation.Value;
            if (MatchesTarget(current))
            {
                policyPersisted = true;
            }
            else if (current == baseline)
            {
                throw new CloudWriteCommitUnknownException();
            }
            else
            {
                throw new CloudWriteConflictException();
            }
        }

        if (!policyPersisted)
        {
            throw new CloudWriteCommitUnknownException();
        }

        await ApplyPolicyToExistingComponents(cancellationToken);

        return Result.Success(new ClientReleaseRetentionPolicyDto(
            request.MaxVersionsPerComponent,
            updatedAtUtc));

        async Task<bool> ExecuteAttemptAsync(
            CancellationToken callbackCancellationToken)
        {
            var currentObservation =
                await CloudWriteCommitRecovery
                    .TryObserveOptionalAttemptAsync(
                        observationReader
                            .ObserveRetentionPolicyAsync,
                        callbackCancellationToken)
                ?? throw new CloudWriteCommitUnknownException();
            var current = currentObservation.Value;
            if (MatchesTarget(current))
            {
                return true;
            }

            if (current != baseline)
            {
                throw new CloudWriteConflictException();
            }

            var policy = await policyRepository.GetSingleOrDefaultAsync(
                new ClientReleaseRetentionPolicyByIdSpec(),
                callbackCancellationToken);
            if (baseline is null)
            {
                if (policy is not null)
                {
                    throw new CloudWriteConflictException();
                }

                policyRepository.Add(new ClientReleaseRetentionPolicy(
                    request.MaxVersionsPerComponent,
                    updatedAtUtc));
            }
            else
            {
                if (policy is null
                    || new ClientReleaseRetentionPolicyWriteState(
                        policy.Id,
                        policy.MaxVersionsPerComponent,
                        ClientReleaseWriteCommitRecovery.NormalizeUtc(
                            policy.UpdatedAtUtc),
                        policy.RowVersion) != baseline)
                {
                    throw new CloudWriteConflictException();
                }

                policy.Update(
                    request.MaxVersionsPerComponent,
                    updatedAtUtc);
            }

            await policyRepository.SaveChangesAsync(
                callbackCancellationToken);
            return true;
        }

        bool MatchesTarget(
            ClientReleaseRetentionPolicyWriteState? current)
            => current is not null
               && current.Id == ClientReleaseRetentionPolicy.SingletonId
               && current.MaxVersionsPerComponent
               == request.MaxVersionsPerComponent
               && current.UpdatedAtUtc == updatedAtUtc;
    }

    private async Task ApplyPolicyToExistingComponents(CancellationToken cancellationToken)
    {
        var components = await componentRepository.GetListAsync(
            new ClientReleaseComponentsByChannelSpec(null, null, onlyPublished: false, includeArchived: true),
            cancellationToken);

        foreach (var component in components.Where(component => component.ComponentKind == ClientReleaseComponentKind.Host))
        {
            await retentionService.ApplyHostPolicyAsync(
                component.Channel,
                component.TargetRuntime,
                cancellationToken);
        }

        foreach (var component in components.Where(component => component.ComponentKind == ClientReleaseComponentKind.Plugin))
        {
            await retentionService.ApplyPluginPolicyAsync(
                component.ComponentKey,
                component.Channel,
                component.TargetRuntime,
                cancellationToken);
        }
    }
}
