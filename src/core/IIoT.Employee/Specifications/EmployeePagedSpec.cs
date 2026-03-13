using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Employee.Specifications;

/// <summary>
/// 专用查询规约：查询员工分页列表 (支持按工号或姓名模糊搜索)
/// </summary>
public class EmployeePagedSpec : Specification<Aggregates.Employees.Employee>
{
    /// <param name="skip">跳过条数</param>
    /// <param name="take">获取条数</param>
    /// <param name="keyword">关键字 (匹配工号或姓名)</param>
    /// <param name="isPaging">是否启用分页 (查总数时传 false)</param>
    public EmployeePagedSpec(int skip, int take, string? keyword = null, bool isPaging = true)
    {
        // 如果传了关键字，按工号或姓名模糊匹配
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            FilterCondition = e => e.EmployeeNo.Contains(keyword) || e.RealName.Contains(keyword);
        }

        // 必须有排序才能做 EF Core 分页
        SetOrderBy(e => e.EmployeeNo);

        // 只有明确要分页时才装载分页参数
        if (isPaging)
        {
            SetPaging(skip, take);
        }
    }
}