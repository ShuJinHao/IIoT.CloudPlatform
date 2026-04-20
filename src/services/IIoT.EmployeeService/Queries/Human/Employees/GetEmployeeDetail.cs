using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.Employees.Specifications;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Queries.Employees;

public record EmployeeDetailDto(
    Guid Id,
    string EmployeeNo,
    string RealName,
    bool IsActive,
    List<Guid> DeviceIds
);

[AuthorizeRequirement("Employee.Read")]
public record GetEmployeeDetailQuery(Guid EmployeeId) : IHumanQuery<Result<EmployeeDetailDto>>;

public class GetEmployeeDetailHandler(
    IReadRepository<Employee> employeeRepository
) : IQueryHandler<GetEmployeeDetailQuery, Result<EmployeeDetailDto>>
{
    public async Task<Result<EmployeeDetailDto>> Handle(
        GetEmployeeDetailQuery request,
        CancellationToken cancellationToken)
    {
        var employee = await employeeRepository.GetSingleOrDefaultAsync(
            new EmployeeWithAccessesSpec(request.EmployeeId),
            cancellationToken);

        if (employee is null)
            return Result.Failure("未找到该员工档案");

        var dto = new EmployeeDetailDto(
            employee.Id,
            employee.EmployeeNo,
            employee.RealName,
            employee.IsActive,
            employee.DeviceAccesses.Select(d => d.DeviceId).ToList()
        );

        return Result.Success(dto);
    }
}

