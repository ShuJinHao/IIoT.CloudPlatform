using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.MasterData.Specifications;

public class MfgProcessPagedSpec : Specification<MfgProcess>
{
    public MfgProcessPagedSpec(int skip, int take, string? keyword = null, bool isPaging = true)
    {
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            FilterCondition = p => p.ProcessCode.Contains(keyword) || p.ProcessName.Contains(keyword);
        }

        SetOrderBy(p => p.ProcessCode);

        if (isPaging)
        {
            SetPaging(skip, take);
        }
    }
}
