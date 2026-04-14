using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
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
    IIdentityPasswordService identityPasswordService
) : ICommandHandler<ResetPasswordCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        return await identityPasswordService.ResetPasswordAsync(
            request.UserId,
            request.NewPassword,
            cancellationToken);
    }
}
