using IIoT.Application.Contracts;
using IIoT.Core.Employee.Aggregates.MfgProcesses;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands.MfgProcesses;

/// <summary>
/// 业务指令：更新工序基础档案 (编码 + 名称)
/// </summary>
[AuthorizeRequirement("Process.Update")]
public record UpdateMfgProcessCommand(
    Guid ProcessId,
    string ProcessCode,
    string ProcessName
) : ICommand<Result<bool>>;

public class UpdateMfgProcessHandler(
    IDataQueryService dataQueryService,
    IRepository<MfgProcess> processRepository,
    ICacheService cacheService
) : ICommandHandler<UpdateMfgProcessCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(UpdateMfgProcessCommand request, CancellationToken cancellationToken)
    {
        // 1. 查出待修改的工序实体
        var process = await processRepository.GetByIdAsync(request.ProcessId, cancellationToken);

        if (process == null)
        {
            return Result.Failure("未找到目标工序档案");
        }

        // 2. 极速无锁校验：编码唯一性 (排除自身)
        var codeExists = await dataQueryService.AnyAsync(
            dataQueryService.MfgProcesses.Where(p => p.ProcessCode == request.ProcessCode && p.Id != request.ProcessId)
        );
        if (codeExists)
        {
            return Result.Failure($"工序编码 [{request.ProcessCode}] 已被其他工序占用");
        }

        // 3. 更新实体属性
        process.ProcessCode = request.ProcessCode;
        process.ProcessName = request.ProcessName;

        // 4. 持久化
        processRepository.Update(process);
        var affected = await processRepository.SaveChangesAsync(cancellationToken);

        // ==========================================
        // 🌟 5. 缓存双杀：工序变更后爆破全量缓存
        // ==========================================
        if (affected > 0)
        {
            await cacheService.RemoveAsync("iiot:mfgprocess:v1:all", cancellationToken);
        }

        return Result.Success(true);
    }
}
