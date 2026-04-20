using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.Employees.Specifications;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands.Employees;

[AuthorizeRequirement("Employee.Update")]
[DistributedLock("iiot:lock:employee:{EmployeeId}", TimeoutSeconds = 5)]
public record UpdateEmployeeProfileCommand(
    Guid EmployeeId,
    string RealName,
    bool IsActive
) : IHumanCommand<Result<bool>>;

public class UpdateEmployeeProfileHandler(
    IRepository<Employee> employeeRepository,
    IIdentityAccountStore identityAccountStore,
    IUnitOfWork unitOfWork,
    IRefreshTokenService refreshTokenService)
    : ICommandHandler<UpdateEmployeeProfileCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        UpdateEmployeeProfileCommand request,
        CancellationToken cancellationToken)
    {
        var realName = request.RealName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(realName))
        {
            return Result.Failure("员工姓名不能为空");
        }

        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken);

            var employee = await employeeRepository.GetSingleOrDefaultAsync(
                new EmployeeWithAccessesSpec(request.EmployeeId),
                cancellationToken);

            if (employee is null)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return Result.Failure("未找到该员工");
            }

            employee.Rename(employee.EmployeeNo, realName);
            if (request.IsActive)
            {
                employee.Activate();
            }
            else
            {
                employee.Deactivate();
            }

            employeeRepository.Update(employee);
            await employeeRepository.SaveChangesAsync(cancellationToken);

            var identityResult = await identityAccountStore.SetEnabledAsync(
                request.EmployeeId,
                request.IsActive,
                cancellationToken);

            if (!identityResult.IsSuccess)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return Result.Failure(identityResult.Errors?.ToArray() ?? ["账号状态同步失败"]);
            }

            if (!request.IsActive)
            {
                await refreshTokenService.RevokeSubjectTokensAsync(
                    IIoTClaimTypes.HumanActor,
                    request.EmployeeId,
                    "employee-deactivated",
                    cancellationToken);
            }

            await unitOfWork.CommitAsync(cancellationToken);
            return Result.Success(true);
        }
        catch (Exception)
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
