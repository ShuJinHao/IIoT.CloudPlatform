using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

public record ChangePasswordCommand(Guid UserId, string CurrentPassword, string NewPassword) : ICommand<Result>;

public class ChangePasswordHandler(IIdentityService identityService) : ICommandHandler<ChangePasswordCommand, Result>
{
    public async Task<Result> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        return await identityService.ChangePasswordAsync(request.UserId, request.CurrentPassword, request.NewPassword);
    }
}