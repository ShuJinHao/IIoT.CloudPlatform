using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.MasterData.Specifications;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.MasterDataService.Commands.Processes;

[AuthorizeRequirement("Process.Delete")]
[DistributedLock("iiot:lock:process:{ProcessId}", TimeoutSeconds = 5)]
public record DeleteProcessCommand(Guid ProcessId) : IHumanCommand<Result<bool>>;

public class DeleteProcessHandler(
    IRepository<MfgProcess> processRepository,
    IProcessReadQueryService processReadQueryService,
    ICacheService cacheService
) : ICommandHandler<DeleteProcessCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        DeleteProcessCommand request,
        CancellationToken cancellationToken)
    {
        var process = await processRepository.GetSingleOrDefaultAsync(
            new MfgProcessByIdSpec(request.ProcessId),
            cancellationToken);

        if (process is null)
        {
            return Result.Failure("未找到目标工序档案");
        }

        var hasDevice = await processReadQueryService.HasDevicesAsync(
            request.ProcessId,
            cancellationToken);

        if (hasDevice)
        {
            return Result.Failure("删除失败: 该工序下仍有关联设备，请先迁移或停用设备");
        }

        var hasRecipe = await processReadQueryService.HasRecipesAsync(
            request.ProcessId,
            cancellationToken);

        if (hasRecipe)
        {
            return Result.Failure("删除失败: 该工序下仍有关联配方，请先停用或迁移配方");
        }

        processRepository.Delete(process);
        var affected = await processRepository.SaveChangesAsync(cancellationToken);

        if (affected > 0)
        {
            await cacheService.RemoveAsync(CacheKeys.ProcessesAll(), cancellationToken);
        }

        return Result.Success(true);
    }
}
