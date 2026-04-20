using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

[AuthorizeRequirement("Employee.Update")]
[DistributedLock("iiot:lock:user-password:{UserId}", TimeoutSeconds = 5)]
public record ResetPasswordCommand(
    Guid UserId,
    string NewPassword
) : IHumanCommand<Result<bool>>;

public class ResetPasswordHandler(
    IIdentityPasswordService identityPasswordService,
    IRefreshTokenService refreshTokenService
) : ICommandHandler<ResetPasswordCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
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

        return result;
    }
}
