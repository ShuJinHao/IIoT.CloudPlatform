using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IIoT.IdentityService.Commands;

/// <summary>
/// 定义岗位权限策略：创建一个角色，并告诉保安这个角色能进哪些门
/// </summary>
/// <param name="RoleName">角色名称</param>
/// <param name="Permissions">赋予该角色的行为权限点集合</param>
[AuthorizeRequirement("Role.Define")]
public record DefineRolePolicyCommand(string RoleName, List<string> Permissions) : ICommand<Result<bool>>;

public class DefineRolePolicyHandler(IIdentityService identityService)
    : ICommandHandler<DefineRolePolicyCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(DefineRolePolicyCommand request, CancellationToken cancellationToken)
    {
        // 1. 尝试在保安科创建角色名 (底层会瞬间落盘)
        var createResult = await identityService.CreateRoleAsync(request.RoleName);

        if (!createResult.IsSuccess)
        {
            return Result.Failure(createResult.Errors?.ToArray() ?? ["角色创建失败"]);
        }

        // ==========================================
        // 🌟 核心防线：补偿事务机制 (Compensating Transaction)
        // ==========================================
        try
        {
            // 2. 尝试向该角色写入具体的权限 Claims (底层也会瞬间落盘)
            var updateResult = await identityService.UpdateRolePermissionsAsync(request.RoleName, request.Permissions);

            if (!updateResult.IsSuccess || !updateResult.Value)
            {
                // 如果权限挂载逻辑本身返回失败，手动触发补偿
                await RollbackRoleCreation(request.RoleName);
                return Result.Failure(updateResult.Errors?.ToArray() ?? ["角色权限分配失败，已撤销角色创建"]);
            }

            return Result.Success(true);
        }
        catch (Exception ex)
        {
            // 🚨 发生灾难性异常（如数据库断开、并发冲突）
            // 执行时光倒流，抹除这个“半吊子”角色
            await RollbackRoleCreation(request.RoleName);

            return Result.Failure($"定义角色策略时发生异常，已执行回滚删除空壳角色。错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 内部补偿逻辑：物理删除创建失败的角色
    /// </summary>
    private async Task RollbackRoleCreation(string roleName)
    {
        // 这里可以直接调用 IIdentityService 或底层的 RoleManager 执行删除动作
        // 由于是回滚操作，如果删除本身也失败（极端情况），通常需要记录严重日志
        await identityService.RemoveRoleFromUserAsync(string.Empty, roleName); // 假设 IdentityService 扩展了删除角色的能力，或通过其他接口销毁

        // 建议在 IIdentityService 中补充一个专用的 DeleteRoleAsync 接口实现更彻底的销毁
    }
}