using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Production.Specifications.ClientReleases;

public sealed class ClientReleaseRetentionPolicyByIdSpec : Specification<ClientReleaseRetentionPolicy>
{
    public ClientReleaseRetentionPolicyByIdSpec()
    {
        FilterCondition = policy => policy.Id == ClientReleaseRetentionPolicy.SingletonId;
    }
}
