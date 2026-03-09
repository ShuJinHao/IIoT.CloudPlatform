using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

// 只有具备 User.AssignRole 权限的人才能操作
[AuthorizeRequirement("User.AssignRole")]
public record AssignUserRoleCommand(string EmployeeNo, string RoleName) : ICommand<Result<bool>>;

public class AssignUserRoleHandler(IIdentityService identityService)
    : ICommandHandler<AssignUserRoleCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(AssignUserRoleCommand request, CancellationToken cancellationToken)
    {
        // 直接调用底层接口写入关联表
        return await identityService.AssignRoleToUserAsync(request.EmployeeNo, request.RoleName);
    }
}