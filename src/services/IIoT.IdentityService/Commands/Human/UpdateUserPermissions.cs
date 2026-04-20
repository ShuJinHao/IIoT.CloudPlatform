using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

/// <summary>
/// 业务指令：管理员为指定员工设置个人特批权限点 (全量同步)
/// </summary>
/// <remarks>
/// 个人特批权限与角色权限是并集关系，最终生效权限 = 角色权限 + 个人特批权限。
/// </remarks>
[AuthorizeRequirement("Employee.Update")]
[DistributedLock("iiot:lock:user-permissions:{UserId}", TimeoutSeconds = 5)]
public record UpdateUserPermissionsCommand(
    Guid UserId,
    List<string> Permissions
) : IHumanCommand<Result<bool>>;

public class UpdateUserPermissionsHandler(
    IRolePolicyService rolePolicyService,
    ICacheService cacheService
) : ICommandHandler<UpdateUserPermissionsCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(UpdateUserPermissionsCommand request, CancellationToken cancellationToken)
    {
        var result = await rolePolicyService.UpdateUserPersonalPermissionsAsync(request.UserId, request.Permissions);

        if (result.IsSuccess && result.Value)
        {
            // 缓存双杀：个人权限变更后，爆破该用户的权限缓存，下次登录重新查库
            await cacheService.RemoveAsync(CacheKeys.PermissionByUser(request.UserId), cancellationToken);
        }

        return result;
    }
}
