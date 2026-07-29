using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Employees.Specifications;

public sealed class EmployeeByEmployeeNoSpec
    : Specification<Aggregates.Employees.Employee>
{
    public EmployeeByEmployeeNoSpec(string employeeNo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(employeeNo);
        var normalizedEmployeeNo = employeeNo.Trim();
        FilterCondition = employee => employee.EmployeeNo == normalizedEmployeeNo;
        AddInclude(employee => employee.DeviceAccesses);
    }
}
