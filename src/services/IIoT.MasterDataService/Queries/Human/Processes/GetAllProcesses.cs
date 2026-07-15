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
        var dtos = await cacheService.GetOrSetAsync<List<ProcessSelectDto>>(
            CacheKeys.ProcessesAll(),
            async factoryCancellationToken =>
            {
                var list = await processRepository.GetListAsync(
                    new MfgProcessAllSpec(),
                    factoryCancellationToken);
                return list
                    .Select(process => new ProcessSelectDto(
                        process.Id,
                        process.ProcessCode,
                        process.ProcessName))
                    .ToList();
            },
            static value => value is not null,
            TimeSpan.FromHours(4),
            cancellationToken);

        return Result.Success(dtos
            ?? throw new InvalidOperationException("Process cache factory returned null."));
    }
}
