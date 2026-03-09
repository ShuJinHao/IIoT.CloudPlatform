using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

// 定义岗位权限策略：创建一个角色，并告诉保安这个角色能进哪些门
[AuthorizeRequirement("Role.Define")]
public record DefineRolePolicyCommand(string RoleName, List<string> Permissions) : ICommand<Result<bool>>;

public class DefineRolePolicyHandler(IIdentityService identityService)
    : ICommandHandler<DefineRolePolicyCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(DefineRolePolicyCommand request, CancellationToken cancellationToken)
    {
        // 1. 创建角色名
        var createResult = await identityService.CreateRoleAsync(request.RoleName);
        if (!createResult.IsSuccess) return Result.Failure(createResult.Errors?.ToArray() ?? ["角色创建失败"]);

        // 2. 写入权限 Claims
        return await identityService.UpdateRolePermissionsAsync(request.RoleName, request.Permissions);
    }
}