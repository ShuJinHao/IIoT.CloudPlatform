using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.ClientReleases;

[AuthorizeRequirement(ClientReleasePermissions.Manage)]
public sealed record UpdateClientReleaseRetentionPolicyCommand(int MaxVersionsPerComponent)
    : IHumanCommand<Result<ClientReleaseRetentionPolicyDto>>;

public sealed class UpdateClientReleaseRetentionPolicyHandler(
    IRepository<ClientReleaseRetentionPolicy> policyRepository,
    IReadRepository<ClientHostRelease> hostReleaseRepository,
    IReadRepository<ClientPluginRelease> pluginReleaseRepository,
    IClientReleaseRetentionService retentionService)
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

        var policy = await policyRepository.GetSingleOrDefaultAsync(
            new ClientReleaseRetentionPolicyByIdSpec(),
            cancellationToken);

        if (policy is null)
        {
            policy = new ClientReleaseRetentionPolicy(request.MaxVersionsPerComponent);
            policyRepository.Add(policy);
        }
        else
        {
            policy.Update(request.MaxVersionsPerComponent);
        }

        await policyRepository.SaveChangesAsync(cancellationToken);
        await ApplyPolicyToExistingComponents(cancellationToken);

        return Result.Success(new ClientReleaseRetentionPolicyDto(
            policy.MaxVersionsPerComponent,
            policy.UpdatedAtUtc));
    }

    private async Task ApplyPolicyToExistingComponents(CancellationToken cancellationToken)
    {
        var hostComponents = (await hostReleaseRepository.GetListAsync(
                new ClientHostReleasesByChannelSpec(null, null, onlyPublished: false),
                cancellationToken))
            .Select(release => new { release.Channel, release.TargetRuntime })
            .Distinct()
            .ToList();

        foreach (var component in hostComponents)
        {
            await retentionService.ApplyHostPolicyAsync(
                component.Channel,
                component.TargetRuntime,
                cancellationToken);
        }

        var pluginComponents = (await pluginReleaseRepository.GetListAsync(
                new ClientPluginReleasesByChannelSpec(null, null, onlyPublished: false),
                cancellationToken))
            .Select(release => new { release.ModuleId, release.Channel, release.TargetRuntime })
            .Distinct()
            .ToList();

        foreach (var component in pluginComponents)
        {
            await retentionService.ApplyPluginPolicyAsync(
                component.ModuleId,
                component.Channel,
                component.TargetRuntime,
                cancellationToken);
        }
    }
}
