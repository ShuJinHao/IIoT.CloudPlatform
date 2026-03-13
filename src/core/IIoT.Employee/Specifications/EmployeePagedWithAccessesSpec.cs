using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Employee.Specifications;

/// <summary>
/// 分页查询规约：同时 Include 工序和机台管辖权导航属性
/// 用于列表页一次性拿到计数，彻底消灭 N+1
/// </summary>
public class EmployeePagedWithAccessesSpec : Specification<Aggregates.Employees.Employee>
{
    public EmployeePagedWithAccessesSpec(int skip, int take, string? keyword = null)
    {
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            FilterCondition = e => e.EmployeeNo.Contains(keyword) || e.RealName.Contains(keyword);
        }

        // Include 两个导航属性，内存里直接 .Count 统计数量
        AddInclude(e => e.ProcessAccesses);
        AddInclude(e => e.DeviceAccesses);

        SetOrderBy(e => e.EmployeeNo);
        SetPaging(skip, take);
    }
}