using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Queries;

/// <summary>
/// 交互查询：获取指定员工的个人特批权限点列表 (不含角色继承的权限)
/// </summary>
[AdminOnly]
[AuthorizeRequirement(CloudPermissionCatalog.Employee.Read)]
public record GetUserPersonalPermissionsQuery(Guid UserId)
    : IHumanQuery<Result<List<string>>>, IAdminOnlyAuditRequest
{
    public string AdminAuditOperationType => "User.Permissions.Read";

    public string AdminAuditTargetType => "User";

    public string AdminAuditTargetIdOrKey => UserId.ToString();
}

public class GetUserPersonalPermissionsHandler(
    IRolePolicyService rolePolicyService,
    IAdminTargetGuard adminTargetGuard
) : IQueryHandler<GetUserPersonalPermissionsQuery, Result<List<string>>>
{
    public async Task<Result<List<string>>> Handle(GetUserPersonalPermissionsQuery request, CancellationToken cancellationToken)
    {
        var targetResult = await adminTargetGuard.EnsureMutableNonAdminTargetAsync(
            request.UserId,
            cancellationToken);
        if (!targetResult.IsSuccess)
        {
            return Result.Failure(targetResult.Errors?.ToArray()
                ?? [AdminTargetProtectionErrors.TargetNotFound]);
        }

        var permissions = await rolePolicyService.GetUserPersonalPermissionsAsync(request.UserId);
        var validation = CloudPermissionCatalog.Normalize(permissions);
        return validation.IsValid
            ? Result.Success(validation.Permissions.ToList())
            : Result.Failure("用户个人权限包含未定义声明，请先完成安全清理。");
    }
}
