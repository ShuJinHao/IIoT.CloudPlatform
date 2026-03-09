using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Employee.Specifications;

/// <summary>
/// 专用查询规约：查询员工分页列表 (支持按工号或姓名模糊搜索)
/// </summary>
public class EmployeePagedSpec : Specification<Core.Employee.Aggregates.Employees.Employee>
{
    public EmployeePagedSpec(int skip, int take, string? keyword = null)
    {
        // 1. 如果传了关键字，就按工号或姓名模糊匹配
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            FilterCondition = e => e.EmployeeNo.Contains(keyword) || e.RealName.Contains(keyword);
        }

        // 2. 默认按工号排序 (必须有排序才能做 EF Core 分页)
        SetOrderBy(e => e.EmployeeNo);

        // 3. 开启分页
        SetPaging(skip, take);
    }
}