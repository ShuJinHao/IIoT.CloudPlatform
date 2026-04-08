using IIoT.Core.Employee.Aggregates.MfgProcesses;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Employee.Specifications;

/// <summary>
/// 按 Id 精确获取单个 MfgProcess 聚合根。
/// 命令端用例从 Repository 取出聚合根做修改时使用。
/// </summary>
public sealed class MfgProcessByIdSpec : Specification<MfgProcess>
{
    public MfgProcessByIdSpec(Guid processId)
    {
        FilterCondition = p => p.Id == processId;
    }
}