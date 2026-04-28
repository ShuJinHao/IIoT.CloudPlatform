using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Caching;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

[AuthorizeRequirement("Employee.Update")]
[DistributedLock("iiot:lock:user-permissions:{UserId}", TimeoutSeconds = 5)]
public record UpdateUserPermissionsCommand(
    Guid UserId,
    List<string> Permissions
) : IHumanCommand<Result<bool>>;

public class UpdateUserPermissionsHandler(
    IRolePolicyService rolePolicyService,
    ICacheService cacheService,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService
) : ICommandHandler<UpdateUserPermissionsCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(UpdateUserPermissionsCommand request, CancellationToken cancellationToken)
    {
        var result = await rolePolicyService.UpdateUserPersonalPermissionsAsync(request.UserId, request.Permissions);

        if (result.IsSuccess && result.Value)
        {
            await cacheService.RemoveAsync(CacheKeys.PermissionByUser(request.UserId), cancellationToken);
            await auditTrailService.TryWriteAsync(
                CreateAuditEntry(
                    request,
                    succeeded: true,
                    summary: $"Updated personal permissions for user {request.UserId} with {request.Permissions.Count} entries."),
                cancellationToken);
        }
        else
        {
            await auditTrailService.TryWriteAsync(
                CreateAuditEntry(
                    request,
                    succeeded: false,
                    summary: $"Update personal permissions for user {request.UserId}.",
                    failureReason: string.Join("; ", result.Errors ?? ["User personal permission update failed."])),
                cancellationToken);
        }

        return result;
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
