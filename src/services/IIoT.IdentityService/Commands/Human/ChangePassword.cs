using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

[DistributedLock("iiot:lock:user-password:{UserId}", TimeoutSeconds = 5)]
public record ChangePasswordCommand(Guid UserId, string CurrentPassword, string NewPassword) : IHumanCommand<Result>;

public class ChangePasswordHandler(
    IIdentityPasswordService identityPasswordService,
    IRefreshTokenService refreshTokenService,
    ICurrentUser currentUser) : ICommandHandler<ChangePasswordCommand, Result>
{
    public async Task<Result> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(currentUser.Id, out var currentUserId))
            return Result.Failure("用户凭证异常");

        if (currentUserId != request.UserId)
            return Result.Failure("仅允许修改当前登录用户自己的密码");

        var result = await identityPasswordService.ChangePasswordAsync(
            request.UserId,
            request.CurrentPassword,
            request.NewPassword,
            cancellationToken);

        if (result.IsSuccess)
        {
            await refreshTokenService.RevokeSubjectTokensAsync(
                IIoTClaimTypes.HumanActor,
                request.UserId,
                "password-changed",
                cancellationToken);
        }

        return result;
    }
}
