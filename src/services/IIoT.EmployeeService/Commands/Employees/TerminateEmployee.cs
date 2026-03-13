using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands.Employees;

[AuthorizeRequirement("Employee.Terminate")]
public record TerminateEmployeeCommand(Guid EmployeeId) : ICommand<Result>;

public class TerminateEmployeeHandler(
    IAccountService accountService
) : ICommandHandler<TerminateEmployeeCommand, Result>
{
    public async Task<Result> Handle(TerminateEmployeeCommand request, CancellationToken cancellationToken)
    {
        var result = await accountService.DeleteUserAsync(request.EmployeeId);

        if (!result.IsSuccess)
        {
            return Result.Failure(result.Errors?.ToArray() ?? ["\u5458\u5DE5\u8D26\u53F7\u9500\u6BC1\u5931\u8D25\uFF0C\u53EF\u80FD\u4E0D\u5B58\u5728"]);
        }

        return Result.Success();
    }
}
