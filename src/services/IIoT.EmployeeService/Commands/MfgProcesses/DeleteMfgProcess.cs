using IIoT.Core.Employee.Aggregates.MfgProcesses;
using IIoT.Core.Employee.Specifications;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands.MfgProcesses;

/// <summary>
/// 业务指令:删除工序 (硬删除)
/// </summary>
/// <remarks>
/// 工序一旦被设备或配方引用,禁止删除,防止数据孤岛。
/// 跨聚合的引用检查通过 IDataQueryService 直接 LINQ 查询完成,
/// 走 EF Core 翻译为 EXISTS 子句,Application 层不需要物理依赖 Production 域 Repository。
/// </remarks>
[AuthorizeRequirement("Process.Delete")]
[DistributedLock("iiot:lock:mfg-process:{ProcessId}", TimeoutSeconds = 5)]
public record DeleteMfgProcessCommand(Guid ProcessId) : ICommand<Result<bool>>;

public class DeleteMfgProcessHandler(
    IRepository<MfgProcess> processRepository,
    IDataQueryService dataQueryService,
    ICacheService cacheService
) : ICommandHandler<DeleteMfgProcessCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        DeleteMfgProcessCommand request,
        CancellationToken cancellationToken)
    {
        var process = await processRepository.GetSingleOrDefaultAsync(
            new MfgProcessByIdSpec(request.ProcessId),
            cancellationToken);

        if (process is null)
            return Result.Failure("未找到目标工序档案");

        var hasDevice = await dataQueryService.AnyAsync(
            dataQueryService.Devices.Where(d => d.ProcessId == request.ProcessId));

        if (hasDevice)
            return Result.Failure("删除失败:该工序下仍有设备挂载,请先迁移或停用相关设备");

        var hasRecipe = await dataQueryService.AnyAsync(
            dataQueryService.Recipes.Where(r => r.ProcessId == request.ProcessId));

        if (hasRecipe)
            return Result.Failure("删除失败:该工序下仍有配方关联,请先停用或迁移相关配方");

        processRepository.Delete(process);
        var affected = await processRepository.SaveChangesAsync(cancellationToken);

        if (affected > 0)
        {
            await cacheService.RemoveAsync("iiot:mfgprocess:v1:all", cancellationToken);
        }

        return Result.Success(true);
    }
}