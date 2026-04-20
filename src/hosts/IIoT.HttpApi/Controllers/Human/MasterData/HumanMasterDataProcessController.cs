using IIoT.HttpApi.Infrastructure;
using IIoT.MasterDataService.Commands.Processes;
using IIoT.MasterDataService.Queries.Processes;
using IIoT.SharedKernel.Paging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

[Authorize]
[Route("api/v1/human/master-data/processes")]
[ApiController]
[Tags("Human Master Data Processes")]
public class HumanMasterDataProcessController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPagedList([FromQuery] Pagination pagination, [FromQuery] string? keyword = null)
    {
        pagination ??= new Pagination();
        return ReturnResult(await Sender.Send(new GetProcessPagedListQuery(pagination, keyword)));
    }

    [HttpGet("all")]
    public async Task<IActionResult> GetAll()
    {
        return ReturnResult(await Sender.Send(new GetAllProcessesQuery()));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProcessCommand command)
    {
        return ReturnResult(
            await Sender.Send(command),
            processId => $"/api/v1/human/master-data/processes/{processId}");
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateProcessCommand command)
    {
        return ReturnResult(await Sender.Send(command with { ProcessId = id }));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        return ReturnResult(await Sender.Send(new DeleteProcessCommand(id)));
    }
}
