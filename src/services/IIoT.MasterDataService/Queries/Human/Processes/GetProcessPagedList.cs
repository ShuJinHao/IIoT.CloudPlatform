using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.MasterData.Specifications;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.MasterDataService.Queries.Processes;

public record ProcessListItemDto(
    Guid Id,
    string ProcessCode,
    string ProcessName
);

[AuthorizeRequirement("Process.Read")]
public record GetProcessPagedListQuery(Pagination PaginationParams, string? Keyword = null)
    : IHumanQuery<Result<PagedList<ProcessListItemDto>>>;

public class GetProcessPagedListHandler(
    IReadRepository<MfgProcess> processRepository
) : IQueryHandler<GetProcessPagedListQuery, Result<PagedList<ProcessListItemDto>>>
{
    public async Task<Result<PagedList<ProcessListItemDto>>> Handle(
        GetProcessPagedListQuery request,
        CancellationToken cancellationToken)
    {
        var skip = (request.PaginationParams.PageNumber - 1) * request.PaginationParams.PageSize;
        var take = request.PaginationParams.PageSize;

        var countSpec = new MfgProcessPagedSpec(0, 0, request.Keyword, isPaging: false);
        var totalCount = await processRepository.CountAsync(countSpec, cancellationToken);

        List<MfgProcess> list = [];
        if (totalCount > 0)
        {
            var pagedSpec = new MfgProcessPagedSpec(skip, take, request.Keyword, isPaging: true);
            list = await processRepository.GetListAsync(pagedSpec, cancellationToken);
        }

        var dtos = list.Select(p => new ProcessListItemDto(
            p.Id,
            p.ProcessCode,
            p.ProcessName
        )).ToList();

        return Result.Success(new PagedList<ProcessListItemDto>(dtos, totalCount, request.PaginationParams));
    }
}
