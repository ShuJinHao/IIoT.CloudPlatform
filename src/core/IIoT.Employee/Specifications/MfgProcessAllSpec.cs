using IIoT.Core.Employee.Aggregates.MfgProcesses;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Employee.Specifications;

/// <summary>
/// 专用查询规约：获取全量工序列表 (供下拉选择器使用)
/// </summary>
public class MfgProcessAllSpec : Specification<MfgProcess>
{
    public MfgProcessAllSpec()
    {
        // 按工序编码排序，保证前端下拉列表稳定有序
        SetOrderBy(p => p.ProcessCode);
    }
}
