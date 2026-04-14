using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IIoT.IdentityService.Commands;

[AuthorizeRequirement("Role.Define")]
[DistributedLock("iiot:lock:role:{RoleName}", TimeoutSeconds = 5)]
public record DefineRolePolicyCommand(string RoleName, List<string> Permissions) : IHumanCommand<Result<bool>>;

public class DefineRolePolicyHandler(
    IRolePolicyService rolePolicyService,
    ICacheService cacheService
) : ICommandHandler<DefineRolePolicyCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(DefineRolePolicyCommand request, CancellationToken cancellationToken)
    {
        var createResult = await rolePolicyService.CreateRoleAsync(request.RoleName);

        if (!createResult.IsSuccess)
        {
            return Result.Failure(createResult.Errors?.ToArray() ?? ["角色创建失败"]);
        }

        try
        {
            var updateResult = await rolePolicyService.UpdateRolePermissionsAsync(request.RoleName, request.Permissions);

            if (!updateResult.IsSuccess || !updateResult.Value)
            {
                await rolePolicyService.RemoveRoleFromUserAsync(string.Empty, request.RoleName);
                return Result.Failure(updateResult.Errors?.ToArray() ?? ["角色权限分配失败，已撤销角色创建"]);
            }

            await cacheService.RemoveByPatternAsync(
                CacheKeys.PermissionByUserPattern(),
                cancellationToken);
            await cacheService.RemoveAsync(
                CacheKeys.AllDefinedPermissions(),
                cancellationToken);

            return Result.Success(true);
        }
        catch (Exception ex)
        {
            await rolePolicyService.RemoveRoleFromUserAsync(string.Empty, request.RoleName);
            return Result.Failure($"定义角色策略时发生异常，已执行回滚删除空壳角色。错误: {ex.Message}");
        }
    }
}
