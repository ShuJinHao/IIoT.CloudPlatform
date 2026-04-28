using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Queries;

public record RolePermissionsDto(
    string RoleName,
    List<string> Permissions
);

[AuthorizeRequirement("Role.Define")]
public record GetRolePermissionsQuery(string RoleName) : IHumanQuery<Result<RolePermissionsDto>>;

public class GetRolePermissionsHandler(
    IRolePolicyService rolePolicyService
) : IQueryHandler<GetRolePermissionsQuery, Result<RolePermissionsDto>>
{
    public async Task<Result<RolePermissionsDto>> Handle(GetRolePermissionsQuery request, CancellationToken cancellationToken)
    {
        var permissions = await rolePolicyService.GetRolePermissionsAsync(request.RoleName);

        return Result.Success(new RolePermissionsDto(request.RoleName, permissions ?? []));
    }
}
