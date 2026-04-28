using IIoT.HttpApi.Infrastructure;
using IIoT.MasterDataService.Commands.Processes;
using IIoT.MasterDataService.Queries.Processes;
using IIoT.SharedKernel.Paging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[Authorize]
[EnableRateLimiting(HttpApiRateLimitPolicies.GeneralApi)]
[Route("api/v1/human/master-data/processes")]
[ApiController]
[Tags("Human Master Data Processes")]
public class HumanMasterDataProcessController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPagedList(
        [FromQuery] Pagination pagination,
        [FromQuery] string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        pagination ??= new Pagination();
        return ReturnResult(await Sender.Send(new GetProcessPagedListQuery(pagination, keyword), cancellationToken));
    }

    [HttpGet("all")]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new GetAllProcessesQuery(), cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateProcessCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(
            await Sender.Send(command, cancellationToken),
            processId => $"/api/v1/human/master-data/processes/{processId}");
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(
        [FromRoute] Guid id,
        [FromBody] UpdateProcessCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(command with { ProcessId = id }, cancellationToken));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new DeleteProcessCommand(id), cancellationToken));
    }
}
