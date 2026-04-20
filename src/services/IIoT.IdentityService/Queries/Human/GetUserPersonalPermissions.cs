using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Queries;

/// <summary>
/// 交互查询：获取指定员工的个人特批权限点列表 (不含角色继承的权限)
/// </summary>
[AuthorizeRequirement("Employee.Read")]
public record GetUserPersonalPermissionsQuery(Guid UserId) : IHumanQuery<Result<List<string>>>;

public class GetUserPersonalPermissionsHandler(
    IRolePolicyService rolePolicyService
) : IQueryHandler<GetUserPersonalPermissionsQuery, Result<List<string>>>
{
    public async Task<Result<List<string>>> Handle(GetUserPersonalPermissionsQuery request, CancellationToken cancellationToken)
    {
        var permissions = await rolePolicyService.GetUserPersonalPermissionsAsync(request.UserId);
        return Result.Success(permissions);
    }
}
