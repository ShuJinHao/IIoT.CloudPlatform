using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Employee.Specifications;

/// <summary>
/// 专用查询规约：查询员工，并联级加载其名下所有的管辖权
/// </summary>
public class EmployeeWithAccessesSpec : Specification<Aggregates.Employees.Employee>
{
    public EmployeeWithAccessesSpec(Guid employeeId)
    {
        // 1. 过滤条件
        FilterCondition = e => e.Id == employeeId;

        // 2. 要求联级加载的数据 (极其清爽的写法)
        AddInclude(e => e.ProcessAccesses);
        AddInclude(e => e.DeviceAccesses);
    }
}