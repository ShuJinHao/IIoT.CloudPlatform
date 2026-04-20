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

[AuthorizeRequirement("Process.Update")]
[DistributedLock("iiot:lock:process-code:{ProcessCode}", TimeoutSeconds = 5)]
public record UpdateProcessCommand(
    Guid ProcessId,
    string ProcessCode,
    string ProcessName
) : IHumanCommand<Result<bool>>;

public class UpdateProcessHandler(
    IRepository<MfgProcess> processRepository,
    IProcessReadQueryService processReadQueryService,
    ICacheService cacheService
) : ICommandHandler<UpdateProcessCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        UpdateProcessCommand request,
        CancellationToken cancellationToken)
    {
        var code = request.ProcessCode?.Trim() ?? string.Empty;
        var name = request.ProcessName?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(code))
        {
            return Result.Failure("工序编码不能为空");
        }

        if (string.IsNullOrEmpty(name))
        {
            return Result.Failure("工序名称不能为空");
        }

        var process = await processRepository.GetSingleOrDefaultAsync(
            new MfgProcessByIdSpec(request.ProcessId),
            cancellationToken);

        if (process is null)
        {
            return Result.Failure("未找到目标工序档案");
        }

        var codeOccupied = await processReadQueryService.CodeExistsAsync(
            code,
            request.ProcessId,
            cancellationToken);

        if (codeOccupied)
        {
            return Result.Failure($"工序编码 [{code}] 已被其他工序占用");
        }

        process.Rename(code, name);

        processRepository.Update(process);
        var affected = await processRepository.SaveChangesAsync(cancellationToken);

        if (affected > 0)
        {
            await cacheService.RemoveAsync(CacheKeys.ProcessesAll(), cancellationToken);
        }

        return Result.Success(true);
    }
}
