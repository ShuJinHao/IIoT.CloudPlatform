using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.Queries.ClientReleases;

[AuthorizeRequirement("Device.Read")]
public sealed record GetClientReleaseRetentionPolicyQuery
    : IHumanQuery<Result<ClientReleaseRetentionPolicyDto>>;

public sealed class GetClientReleaseRetentionPolicyHandler(
    IReadRepository<ClientReleaseRetentionPolicy> repository,
    IOptions<EdgeReleaseRetentionOptions> options)
    : IQueryHandler<GetClientReleaseRetentionPolicyQuery, Result<ClientReleaseRetentionPolicyDto>>
{
    public async Task<Result<ClientReleaseRetentionPolicyDto>> Handle(
        GetClientReleaseRetentionPolicyQuery request,
        CancellationToken cancellationToken)
    {
        var policy = await repository.GetSingleOrDefaultAsync(
            new ClientReleaseRetentionPolicyByIdSpec(),
            cancellationToken);

        return Result.Success(policy is null
            ? new ClientReleaseRetentionPolicyDto(options.Value.MaxVersionsPerComponent, DateTime.UnixEpoch)
            : new ClientReleaseRetentionPolicyDto(policy.MaxVersionsPerComponent, policy.UpdatedAtUtc));
    }
}
