using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

[DistributedLock("iiot:lock:user-password:{UserId}", TimeoutSeconds = 5)]
public record ChangePasswordCommand(Guid UserId, string CurrentPassword, string NewPassword) : IHumanCommand<Result>;

public class ChangePasswordHandler(IIdentityPasswordService identityPasswordService) : ICommandHandler<ChangePasswordCommand, Result>
{
    public async Task<Result> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        return await identityPasswordService.ChangePasswordAsync(
            request.UserId,
            request.CurrentPassword,
            request.NewPassword,
            cancellationToken);
    }
}
