using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Caching;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

[AuthorizeRequirement("Role.Update")]
[DistributedLock("iiot:lock:role:{RoleName}", TimeoutSeconds = 5)]
public record UpdateRolePermissionsCommand(string RoleName, List<string> Permissions) : IHumanCommand<Result<bool>>;

public class UpdateRolePermissionsHandler(
    IRolePolicyService rolePolicyService,
    ICacheService cacheService,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService
) : ICommandHandler<UpdateRolePermissionsCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(UpdateRolePermissionsCommand request, CancellationToken cancellationToken)
    {
        if (request.RoleName.Equals(SystemRoles.Admin, StringComparison.OrdinalIgnoreCase))
        {
            return await FailAsync(
                request,
                "System protection: built-in Admin role permissions cannot be modified.",
                cancellationToken);
        }

        var result = await rolePolicyService.UpdateRolePermissionsAsync(request.RoleName, request.Permissions);

        if (result.IsSuccess && result.Value)
        {
            await cacheService.RemoveByPatternAsync(
                CacheKeys.PermissionByUserPattern(),
                cancellationToken);
            await cacheService.RemoveAsync(
                CacheKeys.AllDefinedPermissions(),
                cancellationToken);

            await auditTrailService.TryWriteAsync(
                CreateAuditEntry(
                    request,
                    succeeded: true,
                    summary: $"Updated role {request.RoleName} permissions with {request.Permissions.Count} entries."),
                cancellationToken);
        }
        else
        {
            await auditTrailService.TryWriteAsync(
                CreateAuditEntry(
                    request,
                    succeeded: false,
                    summary: $"Update role {request.RoleName} permissions.",
                    failureReason: string.Join("; ", result.Errors ?? ["Role permission update failed."])),
                cancellationToken);
        }

        return result;
    }

    private async Task<Result<bool>> FailAsync(
        UpdateRolePermissionsCommand request,
        string message,
        CancellationToken cancellationToken)
    {
        await auditTrailService.TryWriteAsync(
            CreateAuditEntry(
                request,
                succeeded: false,
                summary: $"Update role {request.RoleName} permissions.",
                failureReason: message),
            cancellationToken);

        return Result.Failure(message);
    }

    private AuditTrailEntry CreateAuditEntry(
        UpdateRolePermissionsCommand request,
        bool succeeded,
        string summary,
        string? failureReason = null)
    {
        return new AuditTrailEntry(
            ParseActorUserId(currentUser.Id),
            currentUser.UserName,
            "Role.Permissions.Update",
            "Role",
            request.RoleName,
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
