using IIoT.Application.Contracts;
using IIoT.Core.Employee.Aggregates.MfgProcesses;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands.MfgProcesses;

/// <summary>
/// 业务指令：创建新的制造工序
/// </summary>
[AuthorizeRequirement("Process.Create")]
public record CreateMfgProcessCommand(
    string ProcessCode,
    string ProcessName
) : ICommand<Result<Guid>>;

public class CreateMfgProcessHandler(
    IDataQueryService dataQueryService,
    IRepository<MfgProcess> processRepository,
    ICacheService cacheService
) : ICommandHandler<CreateMfgProcessCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateMfgProcessCommand request, CancellationToken cancellationToken)
    {
        // ==========================================
        // 🌟 1. 极速无锁校验区 (压榨性能)
        // ==========================================

        var codeExists = await dataQueryService.AnyAsync(
            dataQueryService.MfgProcesses.Where(p => p.ProcessCode == request.ProcessCode)
        );
        if (codeExists)
        {
            return Result.Failure($"工序创建失败：编码 [{request.ProcessCode}] 已存在");
        }

        // ==========================================
        // 🌟 2. 领域对象构建与持久化
        // ==========================================

        var process = new MfgProcess(request.ProcessCode, request.ProcessName);

        processRepository.Add(process);
        var affected = await processRepository.SaveChangesAsync(cancellationToken);

        // ==========================================
        // 🌟 3. 缓存双杀：新工序入库后爆破全量缓存
        // ==========================================
        if (affected > 0)
        {
            await cacheService.RemoveAsync("iiot:mfgprocess:v1:all", cancellationToken);
        }

        return Result.Success(process.Id);
    }
}
