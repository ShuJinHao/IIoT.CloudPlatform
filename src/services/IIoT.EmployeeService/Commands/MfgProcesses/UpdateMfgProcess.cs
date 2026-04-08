using IIoT.Core.Employee.Aggregates.MfgProcesses;
using IIoT.Core.Employee.Specifications;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands.MfgProcesses;

/// <summary>
/// 业务指令:更新工序基础档案 (编码 + 名称)
/// </summary>
[AuthorizeRequirement("Process.Update")]
[DistributedLock("iiot:lock:mfg-process-code:{ProcessCode}", TimeoutSeconds = 5)]
public record UpdateMfgProcessCommand(
    Guid ProcessId,
    string ProcessCode,
    string ProcessName
) : ICommand<Result<bool>>;

public class UpdateMfgProcessHandler(
    IRepository<MfgProcess> processRepository,
    ICacheService cacheService
) : ICommandHandler<UpdateMfgProcessCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        UpdateMfgProcessCommand request,
        CancellationToken cancellationToken)
    {
        var code = request.ProcessCode?.Trim() ?? string.Empty;
        var name = request.ProcessName?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(code))
            return Result.Failure("工序编码不能为空");
        if (string.IsNullOrEmpty(name))
            return Result.Failure("工序名称不能为空");

        var process = await processRepository.GetSingleOrDefaultAsync(
            new MfgProcessByIdSpec(request.ProcessId),
            cancellationToken);

        if (process is null)
            return Result.Failure("未找到目标工序档案");

        var codeOccupied = await processRepository.AnyAsync(
            p => p.ProcessCode == code && p.Id != request.ProcessId,
            cancellationToken);

        if (codeOccupied)
            return Result.Failure($"工序编码 [{code}] 已被其他工序占用");

        process.Rename(code, name);

        processRepository.Update(process);
        var affected = await processRepository.SaveChangesAsync(cancellationToken);

        if (affected > 0)
        {
            await cacheService.RemoveAsync("iiot:mfgprocess:v1:all", cancellationToken);
        }

        return Result.Success(true);
    }
}