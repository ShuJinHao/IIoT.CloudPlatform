using IIoT.Core.Employee.Aggregates.MfgProcesses;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands.MfgProcesses;

/// <summary>
/// 业务指令:创建新的制造工序
/// </summary>
[AuthorizeRequirement("Process.Create")]
[DistributedLock("iiot:lock:mfg-process-code:{ProcessCode}", TimeoutSeconds = 5)]
public record CreateMfgProcessCommand(
    string ProcessCode,
    string ProcessName
) : ICommand<Result<Guid>>;

public class CreateMfgProcessHandler(
    IRepository<MfgProcess> processRepository,
    IDataQueryService dataQueryService,
    ICacheService cacheService
) : ICommandHandler<CreateMfgProcessCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        CreateMfgProcessCommand request,
        CancellationToken cancellationToken)
    {
        var code = request.ProcessCode?.Trim() ?? string.Empty;
        var name = request.ProcessName?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(code))
            return Result.Failure("工序编码不能为空");
        if (string.IsNullOrEmpty(name))
            return Result.Failure("工序名称不能为空");

        var codeExists = await dataQueryService.AnyAsync(
            dataQueryService.MfgProcesses.Where(p => p.ProcessCode == code));

        if (codeExists)
            return Result.Failure($"工序创建失败:编码 [{code}] 已存在");

        var process = new MfgProcess(code, name);

        processRepository.Add(process);
        var affected = await processRepository.SaveChangesAsync(cancellationToken);

        if (affected > 0)
        {
            await cacheService.RemoveAsync("iiot:mfgprocess:v1:all", cancellationToken);
        }

        return Result.Success(process.Id);
    }
}