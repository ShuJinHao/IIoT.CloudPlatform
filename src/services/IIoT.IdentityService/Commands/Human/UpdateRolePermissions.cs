using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

[AuthorizeRequirement(CloudPermissionCatalog.Role.Update)]
[DistributedLock("iiot:lock:role:{RoleName}", TimeoutSeconds = 5)]
public record UpdateRolePermissionsCommand(string RoleName, List<string> Permissions) : IHumanCommand<Result<bool>>;

public class UpdateRolePermissionsHandler(
    IRolePolicyService rolePolicyService,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService
) : ICommandHandler<UpdateRolePermissionsCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(UpdateRolePermissionsCommand request, CancellationToken cancellationToken)
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

        var beforePermissions = await rolePolicyService.GetRolePermissionsAsync(roleName) ?? [];
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
        var result = await rolePolicyService.UpdateRolePermissionsAsync(
            roleName,
            normalizedPermissions);
        var afterPermissions = await rolePolicyService.GetRolePermissionsAsync(roleName)
            ?? (result.IsSuccess && result.Value ? normalizedPermissions : beforePermissions);

        if (result.IsSuccess && result.Value)
        {
            await auditTrailService.TryWriteAsync(
                CreateAuditEntry(
                    roleName,
                    succeeded: true,
                    summary: PermissionAuditSummary.Serialize(
                        "RolePermissionsUpdate",
                        beforePermissions,
                        afterPermissions,
                        normalizedPermissions,
                        "Succeeded")),
                cancellationToken);
        }
        else
        {
            return await FailAsync(
                roleName,
                beforePermissions,
                afterPermissions,
                normalizedPermissions,
                result.Errors?.ToArray() ?? ["Role permission update failed."],
                "PermissionPersistenceFailed",
                0,
                cancellationToken);
        }

        return result;
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
                    "RolePermissionsUpdate",
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
            "Role.Permissions.Update",
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
