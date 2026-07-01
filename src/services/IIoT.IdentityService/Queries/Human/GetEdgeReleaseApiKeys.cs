using IIoT.Services.Contracts;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Queries;

[AuthorizeRequirement(ClientReleasePermissions.Manage)]
public sealed record GetEdgeReleaseApiKeysQuery()
    : IHumanQuery<Result<IReadOnlyList<EdgeReleaseApiKeyListItem>>>;

public sealed class GetEdgeReleaseApiKeysHandler(IEdgeReleaseApiKeyService apiKeyService)
    : IQueryHandler<GetEdgeReleaseApiKeysQuery, Result<IReadOnlyList<EdgeReleaseApiKeyListItem>>>
{
    public async Task<Result<IReadOnlyList<EdgeReleaseApiKeyListItem>>> Handle(
        GetEdgeReleaseApiKeysQuery request,
        CancellationToken cancellationToken)
        => Result.Success(await apiKeyService.GetListAsync(cancellationToken));
}
