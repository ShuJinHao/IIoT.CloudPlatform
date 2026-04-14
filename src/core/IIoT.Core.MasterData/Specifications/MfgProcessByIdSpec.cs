using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.MasterData.Specifications;

public sealed class MfgProcessByIdSpec : Specification<MfgProcess>
{
    public MfgProcessByIdSpec(Guid processId)
    {
        FilterCondition = p => p.Id == processId;
    }
}
