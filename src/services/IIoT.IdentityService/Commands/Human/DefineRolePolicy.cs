using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

[AuthorizeRequirement(CloudPermissionCatalog.Role.Define)]
[DistributedLock("iiot:lock:role:{RoleName}", TimeoutSeconds = 5)]
public record DefineRolePolicyCommand(string RoleName, List<string> Permissions) : IHumanCommand<Result<bool>>;

public class DefineRolePolicyHandler(
    IRolePolicyService rolePolicyService,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService
) : ICommandHandler<DefineRolePolicyCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(DefineRolePolicyCommand request, CancellationToken cancellationToken)
    {
        var roleName = (request.RoleName ?? string.Empty).Trim();
        if (SystemRoles.IsAdminLike(roleName))
        {
            return await FailAsync(
                roleName,
                [],
                [],
                [],
                ["禁止定义、覆盖或修改 Admin 角色。"],
                "AdminRoleProtected",
                0,
                cancellationToken);
        }

        var roleAlreadyExists = await rolePolicyService.RoleExistsAsync(roleName);
        if (roleAlreadyExists)
        {
            var existingPermissions = await rolePolicyService.GetRolePermissionsAsync(roleName) ?? [];
            return await FailAsync(
                roleName,
                existingPermissions,
                existingPermissions,
                [],
                ["角色已存在，定义接口只允许创建新角色。"],
                "RoleAlreadyExists",
                0,
                cancellationToken);
        }

        List<string> beforePermissions = [];
        var validation = CloudPermissionCatalog.NormalizeForTargetRole(
            roleName,
            request.Permissions);
        if (!validation.IsValid)
        {
            return await FailAsync(
                roleName,
                beforePermissions,
                beforePermissions,
                validation.Permissions,
                ["权限集合包含未知权限或 RoleAdmin 不可分配权限，未执行任何修改。"],
                "PermissionNotAssignable",
                validation.RejectedPermissions.Count,
                cancellationToken);
        }

        var normalizedPermissions = validation.Permissions.ToList();
        var createResult = await rolePolicyService.CreateRoleAsync(roleName);

        if (!createResult.IsSuccess)
        {
            return await FailAsync(
                roleName,
                beforePermissions,
                beforePermissions,
                normalizedPermissions,
                createResult.Errors?.ToArray() ?? ["Role creation failed."],
                "RoleCreationFailed",
                0,
                cancellationToken);
        }

        try
        {
            var updateResult = await rolePolicyService.UpdateRolePermissionsAsync(
                roleName,
                normalizedPermissions);

            if (!updateResult.IsSuccess || !updateResult.Value)
            {
                await rolePolicyService.DeleteRoleAsync(roleName);

                return await FailAsync(
                    roleName,
                    beforePermissions,
                    [],
                    normalizedPermissions,
                    updateResult.Errors?.ToArray() ?? ["Role permission assignment failed."],
                    "PermissionPersistenceFailed",
                    0,
                    cancellationToken);
            }

            var afterPermissions = await rolePolicyService.GetRolePermissionsAsync(roleName)
                ?? normalizedPermissions;
            await auditTrailService.TryWriteAsync(
                CreateAuditEntry(
                    roleName,
                    succeeded: true,
                    summary: PermissionAuditSummary.Serialize(
                        "RoleDefine",
                        beforePermissions,
                        afterPermissions,
                        normalizedPermissions,
                        "Succeeded")),
                cancellationToken);

            return Result.Success(true);
        }
        catch (Exception)
        {
            await rolePolicyService.DeleteRoleAsync(roleName);

            return await FailAsync(
                roleName,
                beforePermissions,
                [],
                normalizedPermissions,
                ["角色定义执行失败，已回滚本次新建角色。"],
                "UnexpectedFailure",
                0,
                cancellationToken);
        }
    }

    private async Task<Result<bool>> FailAsync(
        string roleName,
        IEnumerable<string> beforePermissions,
        IEnumerable<string> afterPermissions,
        IEnumerable<string> requestedPermissions,
        string[] errors,
        string reasonCode,
        int rejectedPermissionCount,
        CancellationToken cancellationToken)
    {
        await auditTrailService.TryWriteAsync(
            CreateAuditEntry(
                roleName,
                succeeded: false,
                summary: PermissionAuditSummary.Serialize(
                    "RoleDefine",
                    beforePermissions,
                    afterPermissions,
                    requestedPermissions,
                    "Rejected",
                    reasonCode,
                    rejectedPermissionCount),
                failureReason: string.Join("; ", errors)),
            cancellationToken);

        return Result.Failure(errors);
    }

    private AuditTrailEntry CreateAuditEntry(
        string roleName,
        bool succeeded,
        string summary,
        string? failureReason = null)
    {
        return new AuditTrailEntry(
            ParseActorUserId(currentUser.Id),
            currentUser.UserName,
            "Role.Define",
            "Role",
            roleName,
            DateTime.UtcNow,
            succeeded,
            summary,
            failureReason);
    }

    private static Guid? ParseActorUserId(string? rawUserId)
    {
        return Guid.TryParse(rawUserId, out var actorUserId)
            ? actorUserId
            : null;
    }
}
