using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.MasterData.Specifications;

public class MfgProcessAllSpec : Specification<MfgProcess>
{
    public MfgProcessAllSpec()
    {
        SetOrderBy(p => p.ProcessCode);
    }
}
