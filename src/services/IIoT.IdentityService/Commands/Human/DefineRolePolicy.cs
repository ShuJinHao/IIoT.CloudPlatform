using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Caching;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

[AuthorizeRequirement("Role.Define")]
[DistributedLock("iiot:lock:role:{RoleName}", TimeoutSeconds = 5)]
public record DefineRolePolicyCommand(string RoleName, List<string> Permissions) : IHumanCommand<Result<bool>>;

public class DefineRolePolicyHandler(
    IRolePolicyService rolePolicyService,
    ICacheService cacheService,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService
) : ICommandHandler<DefineRolePolicyCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(DefineRolePolicyCommand request, CancellationToken cancellationToken)
    {
        var roleAlreadyExists = await rolePolicyService.RoleExistsAsync(request.RoleName);
        var createResult = roleAlreadyExists
            ? Result.Success()
            : await rolePolicyService.CreateRoleAsync(request.RoleName);

        if (!createResult.IsSuccess)
        {
            return await FailAsync(
                request,
                createResult.Errors?.ToArray() ?? ["Role creation failed."],
                cancellationToken);
        }

        try
        {
            var updateResult = await rolePolicyService.UpdateRolePermissionsAsync(request.RoleName, request.Permissions);

            if (!updateResult.IsSuccess || !updateResult.Value)
            {
                if (!roleAlreadyExists)
                    await rolePolicyService.DeleteRoleAsync(request.RoleName);

                return await FailAsync(
                    request,
                    updateResult.Errors?.ToArray() ?? ["Role permission assignment failed."],
                    cancellationToken);
            }

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
                    summary: $"Defined role {request.RoleName} with {request.Permissions.Count} permissions."),
                cancellationToken);

            return Result.Success(true);
        }
        catch (Exception ex)
        {
            if (!roleAlreadyExists)
                await rolePolicyService.DeleteRoleAsync(request.RoleName);

            var message = roleAlreadyExists
                ? $"Define role policy failed with exception: {ex.Message}"
                : $"Define role policy failed with exception and rolled back the new role: {ex.Message}";

            return await FailAsync(request, [message], cancellationToken);
        }
    }

    private async Task<Result<bool>> FailAsync(
        DefineRolePolicyCommand request,
        string[] errors,
        CancellationToken cancellationToken)
    {
        await auditTrailService.TryWriteAsync(
            CreateAuditEntry(
                request,
                succeeded: false,
                summary: $"Define role {request.RoleName}.",
                failureReason: string.Join("; ", errors)),
            cancellationToken);

        return Result.Failure(errors);
    }

    private AuditTrailEntry CreateAuditEntry(
        DefineRolePolicyCommand request,
        bool succeeded,
        string summary,
        string? failureReason = null)
    {
        return new AuditTrailEntry(
            ParseActorUserId(currentUser.Id),
            currentUser.UserName,
            "Role.Define",
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
