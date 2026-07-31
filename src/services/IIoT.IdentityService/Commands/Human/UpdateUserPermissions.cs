using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

[AdminOnly]
[AuthorizeRequirement(CloudPermissionCatalog.Employee.UpdateAccess)]
[DistributedLock("iiot:lock:user-permissions:{UserId}", TimeoutSeconds = 5)]
public record UpdateUserPermissionsCommand(
    Guid UserId,
    List<string> Permissions
) : IHumanCommand<Result<bool>>, IAdminOnlyAuditRequest
{
    public string AdminAuditOperationType => "User.Permissions.Update";

    public string AdminAuditTargetType => "User";

    public string AdminAuditTargetIdOrKey => UserId.ToString();
}

public class UpdateUserPermissionsHandler(
    IRolePolicyService rolePolicyService,
    IAdminTargetGuard adminTargetGuard,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService
) : ICommandHandler<UpdateUserPermissionsCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(UpdateUserPermissionsCommand request, CancellationToken cancellationToken)
    {
        var targetResult = await adminTargetGuard.EnsureMutableNonAdminTargetAsync(
            request.UserId,
            cancellationToken);
        if (!targetResult.IsSuccess)
        {
            var errors = targetResult.Errors?.ToArray()
                ?? [AdminTargetProtectionErrors.TargetNotFound];
            return await FailAsync(
                request,
                [],
                [],
                [],
                errors,
                errors.Contains(AdminTargetProtectionErrors.TargetNotFound)
                    ? "TargetNotFound"
                    : "AdminTargetProtected",
                0,
                cancellationToken);
        }

        var beforePermissions = await rolePolicyService.GetUserPersonalPermissionsAsync(request.UserId);
        var validation = CloudPermissionCatalog.Normalize(request.Permissions);
        if (!validation.IsValid)
        {
            return await FailAsync(
                request,
                beforePermissions,
                beforePermissions,
                validation.Permissions,
                ["权限集合包含未知或空白权限，未执行任何修改。"],
                "PermissionNotDefined",
                validation.RejectedPermissions.Count,
                cancellationToken);
        }

        var normalizedPermissions = validation.Permissions.ToList();
        var result = await rolePolicyService.UpdateUserPersonalPermissionsAsync(
            request.UserId,
            normalizedPermissions,
            cancellationToken);
        var afterPermissions = await rolePolicyService.GetUserPersonalPermissionsAsync(request.UserId);

        if (result.IsSuccess && result.Value)
        {
            await auditTrailService.TryWriteAsync(
                CreateAuditEntry(
                    request,
                    succeeded: true,
                    summary: PermissionAuditSummary.Serialize(
                        "UserPersonalPermissionsUpdate",
                        beforePermissions,
                        afterPermissions,
                        normalizedPermissions,
                        "Succeeded")),
                cancellationToken);
        }
        else
        {
            return await FailAsync(
                request,
                beforePermissions,
                afterPermissions,
                normalizedPermissions,
                result.Errors?.ToArray() ?? ["User personal permission update failed."],
                "PersistenceFailure",
                0,
                cancellationToken);
        }

        return result;
    }

    private async Task<Result<bool>> FailAsync(
        UpdateUserPermissionsCommand request,
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
                request,
                succeeded: false,
                summary: PermissionAuditSummary.Serialize(
                    "UserPersonalPermissionsUpdate",
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
        UpdateUserPermissionsCommand request,
        bool succeeded,
        string summary,
        string? failureReason = null)
    {
        return new AuditTrailEntry(
            ParseActorUserId(currentUser.Id),
            currentUser.UserName,
            "User.Permissions.Update",
            "User",
            request.UserId.ToString(),
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
