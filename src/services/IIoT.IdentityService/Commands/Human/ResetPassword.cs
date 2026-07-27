using System.Text.Json;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

[AdminOnly]
[AuthorizeRequirement(CloudPermissionCatalog.Employee.Update)]
[DistributedLock("iiot:lock:user-password:{UserId}", TimeoutSeconds = 5)]
public record ResetPasswordCommand(
    Guid UserId,
    string NewPassword
) : IHumanCommand<Result<bool>>, IAdminOnlyAuditRequest
{
    public string AdminAuditOperationType => "User.Password.Reset";

    public string AdminAuditTargetType => "User";

    public string AdminAuditTargetIdOrKey => UserId.ToString();
}

public class ResetPasswordHandler(
    IIdentityPasswordService identityPasswordService,
    IRefreshTokenService refreshTokenService,
    IAdminTargetGuard adminTargetGuard,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService
) : ICommandHandler<ResetPasswordCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var targetResult = await adminTargetGuard.EnsureMutableNonAdminTargetAsync(
            request.UserId,
            cancellationToken);
        if (!targetResult.IsSuccess)
        {
            var errors = targetResult.Errors?.ToArray()
                ?? [AdminTargetProtectionErrors.TargetNotFound];
            await WriteAuditAsync(
                request.UserId,
                false,
                errors.Contains(AdminTargetProtectionErrors.TargetNotFound)
                    ? "TargetNotFound"
                    : "AdminTargetProtected",
                string.Join("; ", errors),
                cancellationToken);
            return Result.Failure(errors);
        }

        var result = await identityPasswordService.ResetPasswordAsync(
            request.UserId,
            request.NewPassword,
            cancellationToken);

        if (result.IsSuccess && result.Value)
        {
            await refreshTokenService.RevokeSubjectTokensAsync(
                IIoTClaimTypes.HumanActor,
                request.UserId,
                "password-reset",
                cancellationToken);
        }

        await WriteAuditAsync(
            request.UserId,
            result.IsSuccess && result.Value,
            result.IsSuccess && result.Value ? "Succeeded" : "PasswordServiceRejected",
            result.IsSuccess && result.Value ? null : "密码重置失败。",
            cancellationToken);

        return result;
    }

    private Task WriteAuditAsync(
        Guid targetUserId,
        bool succeeded,
        string outcome,
        string? failureReason,
        CancellationToken cancellationToken)
        => auditTrailService.TryWriteAsync(
            new AuditTrailEntry(
                ParseActorUserId(currentUser.Id),
                currentUser.UserName,
                "User.Password.Reset",
                "User",
                targetUserId.ToString(),
                DateTime.UtcNow,
                succeeded,
                JsonSerializer.Serialize(new
                {
                    action = "PasswordReset",
                    outcome
                }),
                failureReason),
            cancellationToken);

    private static Guid? ParseActorUserId(string? rawUserId)
        => Guid.TryParse(rawUserId, out var actorUserId)
            ? actorUserId
            : null;
}
