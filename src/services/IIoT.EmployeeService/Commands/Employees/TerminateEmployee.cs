using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands.Employees;

/// <summary>
/// 业务指令:员工永久注销
/// 同时清理业务档案和身份账号 — 与 Deactivate(临时停用)严格区分。
/// </summary>
[AuthorizeRequirement("Employee.Terminate")]
[DistributedLock("iiot:lock:employee:{EmployeeId}", TimeoutSeconds = 5)]
public record TerminateEmployeeCommand(Guid EmployeeId) : ICommand<Result>;

public class TerminateEmployeeHandler(
    IRepository<Employee> employeeRepository,
    IAccountService accountService
) : ICommandHandler<TerminateEmployeeCommand, Result>
{
    public async Task<Result> Handle(
        TerminateEmployeeCommand request,
        CancellationToken cancellationToken)
    {
        // 1. 先删业务档案。即使后续删 Identity 失败,留下的孤儿 Identity 账号
        //    因为查不到对应 Employee 也无法登录使用,比反过来更安全。
        var employee = await employeeRepository.GetSingleOrDefaultAsync(
            new EmployeeWithAccessesSpec(request.EmployeeId),
            cancellationToken);

        if (employee is not null)
        {
            employeeRepository.Delete(employee);
            await employeeRepository.SaveChangesAsync(cancellationToken);
        }

        // 2. 再删 Identity 账号。Employee 不存在时仍执行此步,
        //    用于补偿"上次删档案成功但删账号失败"的残留场景,实现真正的幂等。
        var identityResult = await accountService.DeleteUserAsync(request.EmployeeId);

        if (!identityResult.IsSuccess)
            return Result.Failure(identityResult.Errors?.ToArray() ?? ["员工账号注销失败"]);

        return Result.Success();
    }
}