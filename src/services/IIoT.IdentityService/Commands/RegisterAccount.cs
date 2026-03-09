using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

// 只负责“保安登记”，返回生成的 Guid 给调用方
public record RegisterAccountCommand(string EmployeeNo, string Password) : ICommand<Result<Guid>>;

public class RegisterAccountHandler(IIdentityService identityService)
    : ICommandHandler<RegisterAccountCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(RegisterAccountCommand request, CancellationToken cancellationToken)
    {
        var sharedId = Guid.NewGuid();
        // 仅仅创建身份，不碰 Employee 仓储
        var result = await identityService.CreateUserAsync(sharedId, request.EmployeeNo, request.Password);

        if (!result.IsSuccess) return Result.Failure(result.Errors?.ToArray() ?? ["账号注册失败"]);

        return Result.Success(sharedId);
    }
}