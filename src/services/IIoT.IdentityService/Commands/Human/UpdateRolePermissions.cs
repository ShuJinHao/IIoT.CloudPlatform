using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

[AuthorizeRequirement("Role.Update")]
[DistributedLock("iiot:lock:role:{RoleName}", TimeoutSeconds = 5)]
public record UpdateRolePermissionsCommand(string RoleName, List<string> Permissions) : IHumanCommand<Result<bool>>;

public class UpdateRolePermissionsHandler(
    IRolePolicyService rolePolicyService,
    ICacheService cacheService
) : ICommandHandler<UpdateRolePermissionsCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(UpdateRolePermissionsCommand request, CancellationToken cancellationToken)
    {
        if (request.RoleName.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure("系统保护：内置 Admin 角色的权限由系统硬编码，禁止修改！");
        }

        var result = await rolePolicyService.UpdateRolePermissionsAsync(request.RoleName, request.Permissions);

        if (result.IsSuccess && result.Value)
        {
            // 角色权限变更后，爆破所有用户的权限缓存（下次请求时重新从 DB 计算）
            // 无法枚举哪些用户属于该角色，用模式删除兜底
            await cacheService.RemoveByPatternAsync(
                CacheKeys.PermissionByUserPattern(),
                cancellationToken);
            await cacheService.RemoveAsync(
                CacheKeys.AllDefinedPermissions(),
                cancellationToken);
        }

        return result;
    }
}
