using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Queries;

[AuthorizeRequirement("Role.Define")]
public record GetAllRolesQuery() : IHumanQuery<Result<IList<string>>>;

public class GetAllRolesHandler(IRolePolicyService rolePolicyService) : IQueryHandler<GetAllRolesQuery, Result<IList<string>>>
{
    public async Task<Result<IList<string>>> Handle(GetAllRolesQuery request, CancellationToken cancellationToken)
    {
        var roles = await rolePolicyService.GetAllRolesAsync();
        return Result.Success(roles);
    }
}
