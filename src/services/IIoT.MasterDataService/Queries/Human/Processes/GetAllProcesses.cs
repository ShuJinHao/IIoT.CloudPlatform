using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.MasterData.Specifications;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.MasterDataService.Queries.Processes;

public record ProcessSelectDto(
    Guid Id,
    string ProcessCode,
    string ProcessName
);

[AuthorizeRequirement("Process.Read")]
public record GetAllProcessesQuery() : IHumanQuery<Result<List<ProcessSelectDto>>>;

public class GetAllProcessesHandler(
    IReadRepository<MfgProcess> processRepository,
    ICacheService cacheService
) : IQueryHandler<GetAllProcessesQuery, Result<List<ProcessSelectDto>>>
{
    public async Task<Result<List<ProcessSelectDto>>> Handle(
        GetAllProcessesQuery request,
        CancellationToken cancellationToken)
    {
        var cached = await cacheService.GetAsync<List<ProcessSelectDto>>(
            CacheKeys.ProcessesAll(), cancellationToken);
        if (cached != null) return Result.Success(cached);

        var list = await processRepository.GetListAsync(new MfgProcessAllSpec(), cancellationToken);
        var dtos = list.Select(p => new ProcessSelectDto(p.Id, p.ProcessCode, p.ProcessName)).ToList();

        await cacheService.SetAsync(
            CacheKeys.ProcessesAll(),
            dtos,
            TimeSpan.FromHours(4),
            cancellationToken);

        return Result.Success(dtos);
    }
}
