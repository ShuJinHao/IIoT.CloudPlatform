using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Options;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace IIoT.IdentityService.Commands;

// 1. 定义指令：哪个角色，分配哪些权限？
// 只有超级管理员（具备 Role.Assign 权限）才能调用这个用例
[AuthorizeRequirement("Role.Assign")]
public record UpdateRolePermissionsCommand(string RoleName, List<string> Permissions) : ICommand<Result<bool>>;

// 2. 业务处理
public class UpdateRolePermissionsHandler(
    IIdentityService identityService,
    ICacheService cacheService, // 🌟 注入缓存服务
    IOptions<PermissionCacheOptions> options) : ICommandHandler<UpdateRolePermissionsCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(UpdateRolePermissionsCommand request, CancellationToken cancellationToken)
    {
        // 1. 核心写入：更新数据库中的权限点
        var result = await identityService.UpdateRolePermissionsAsync(request.RoleName, request.Permissions);

        // 2. 🌟 架构防坑：如果数据库更新成功，必须立刻删掉 Redis 里对应的缓存！
        // 这样下一次管道 (AuthorizationBehavior) 校验时，就会因为缓存没命中而去查最新的数据库
        if (result.IsSuccess)
        {
            var cacheKey = $"{options.Value.KeyPrefix}{request.RoleName}";
            await cacheService.RemoveAsync(cacheKey, cancellationToken);
        }

        return result;
    }
}