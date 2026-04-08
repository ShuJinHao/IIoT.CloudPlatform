using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Services.Common.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Queries.Employees;

public record EmployeeAccessDto(
    List<Guid> ProcessIds,
    List<Guid> DeviceIds
);

[AuthorizeRequirement("Employee.Read")]
public record GetEmployeeAccessQuery(Guid EmployeeId) : IQuery<Result<EmployeeAccessDto>>;

public class GetEmployeeAccessHandler(
    IReadRepository<Employee> employeeRepository
) : IQueryHandler<GetEmployeeAccessQuery, Result<EmployeeAccessDto>>
{
    public async Task<Result<EmployeeAccessDto>> Handle(
        GetEmployeeAccessQuery request,
        CancellationToken cancellationToken)
    {
        var employee = await employeeRepository.GetSingleOrDefaultAsync(
            new EmployeeWithAccessesSpec(request.EmployeeId),
            cancellationToken);

        if (employee is null)
            return Result.Failure("未找到该员工档案");

        var dto = new EmployeeAccessDto(
            employee.ProcessAccesses.Select(p => p.ProcessId).ToList(),
            employee.DeviceAccesses.Select(d => d.DeviceId).ToList()
        );

        return Result.Success(dto);
    }
}