using IIoT.Core.Employee.Aggregates.MfgProcesses;
using IIoT.Core.Employee.Specifications;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Queries.MfgProcesses;

/// <summary>
/// 轻量 DTO：用于下拉选择器的工序简要信息
/// </summary>
public record MfgProcessSelectDto(
    Guid Id,
    string ProcessCode,
    string ProcessName
);

/// <summary>
/// 交互查询：获取全量工序列表 (供设备注册、配方创建、员工管辖权等下拉选择器使用)
/// </summary>
/// <remarks>
/// 工序数量在工厂场景下通常不超过百条，无需分页，直接全量拉取即可。
/// 带 Redis 缓存抗压，工序变更时由 Command 端负责缓存双杀。
/// </remarks>
[AuthorizeRequirement("Process.Read")]
public record GetAllMfgProcessesQuery() : IQuery<Result<List<MfgProcessSelectDto>>>;

public class GetAllMfgProcessesHandler(
    IReadRepository<MfgProcess> processRepository,
    ICacheService cacheService
) : IQueryHandler<GetAllMfgProcessesQuery, Result<List<MfgProcessSelectDto>>>
{
    private const string CacheKey = "iiot:mfgprocess:v1:all";

    public async Task<Result<List<MfgProcessSelectDto>>> Handle(GetAllMfgProcessesQuery request, CancellationToken cancellationToken)
    {
        // Cache-Aside
        var cached = await cacheService.GetAsync<List<MfgProcessSelectDto>>(CacheKey, cancellationToken);
        if (cached != null) return Result.Success(cached);

        // 缓存未命中，使用规约图纸查库 (排序封装在 Spec 里)
        var spec = new MfgProcessAllSpec();
        var list = await processRepository.GetListAsync(spec, cancellationToken);

        var dtos = list.Select(p => new MfgProcessSelectDto(
            p.Id, p.ProcessCode, p.ProcessName
        )).ToList();

        // 回写缓存 (工序数据变动频率极低，4 小时过期)
        await cacheService.SetAsync(CacheKey, dtos, TimeSpan.FromHours(4), cancellationToken);

        return Result.Success(dtos);
    }
}
