using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.MasterDataService.Commands.Processes;

[AuthorizeRequirement("Process.Create")]
[DistributedLock("iiot:lock:process-code:{ProcessCode}", TimeoutSeconds = 5)]
public record CreateProcessCommand(
    string ProcessCode,
    string ProcessName
) : IHumanCommand<Result<Guid>>;

public class CreateProcessHandler(
    IRepository<MfgProcess> processRepository,
    IProcessReadQueryService processReadQueryService,
    ICacheService cacheService
) : ICommandHandler<CreateProcessCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        CreateProcessCommand request,
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

        var codeExists = await processReadQueryService.CodeExistsAsync(
            code,
            cancellationToken: cancellationToken);

        if (codeExists)
        {
            return Result.Failure($"工序创建失败: 编码 [{code}] 已存在");
        }

        var process = new MfgProcess(code, name);

        processRepository.Add(process);
        var affected = await processRepository.SaveChangesAsync(cancellationToken);

        if (affected > 0)
        {
            await cacheService.RemoveAsync(CacheKeys.ProcessesAll(), cancellationToken);
        }

        return Result.Success(process.Id);
    }
}
