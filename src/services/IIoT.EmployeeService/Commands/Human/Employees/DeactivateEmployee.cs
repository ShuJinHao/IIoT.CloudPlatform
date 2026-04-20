using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.Employees.Specifications;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands.Employees;

[AuthorizeRequirement("Employee.Deactivate")]
[DistributedLock("iiot:lock:employee:{EmployeeId}", TimeoutSeconds = 5)]
public record DeactivateEmployeeCommand(Guid EmployeeId) : IHumanCommand<Result>;

public class DeactivateEmployeeHandler(
    IRepository<Employee> employeeRepository,
    IIdentityAccountStore identityAccountStore,
    IUnitOfWork unitOfWork,
    ICacheService cacheService,
    IRefreshTokenService refreshTokenService)
    : ICommandHandler<DeactivateEmployeeCommand, Result>
{
    public async Task<Result> Handle(
        DeactivateEmployeeCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken);

            var employee = await employeeRepository.GetSingleOrDefaultAsync(
                new EmployeeWithAccessesSpec(request.EmployeeId),
                cancellationToken);

            if (employee is null)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return Result.Failure("未找到目标员工档案");
            }

            if (employee.IsActive)
            {
                employee.Deactivate();
                employeeRepository.Update(employee);
                await employeeRepository.SaveChangesAsync(cancellationToken);
            }

            var identityResult = await identityAccountStore.SetEnabledAsync(
                request.EmployeeId,
                false,
                cancellationToken);

            if (!identityResult.IsSuccess)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return Result.Failure(identityResult.Errors?.ToArray() ?? ["员工身份账号停用失败"]);
            }

            await cacheService.RemoveAsync(CacheKeys.DeviceAccessesByUser(request.EmployeeId), cancellationToken);
            await refreshTokenService.RevokeSubjectTokensAsync(
                IIoTClaimTypes.HumanActor,
                request.EmployeeId,
                "employee-deactivated",
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception)
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
