using IIoT.HttpApi.Infrastructure;
using IIoT.MasterDataService.Commands.Processes;
using IIoT.MasterDataService.Queries.Processes;
using IIoT.SharedKernel.Paging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

[Authorize]
[Route("api/v1/human/processes")]
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Tags("Legacy Human Processes")]
public class HumanProcessController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPagedList([FromQuery] Pagination pagination, [FromQuery] string? keyword = null)
    {
        pagination ??= new Pagination();
        var result = await Sender.Send(new GetProcessPagedListQuery(pagination, keyword));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpGet("all")]
    public async Task<IActionResult> GetAll()
    {
        var result = await Sender.Send(new GetAllProcessesQuery());
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProcessCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateProcessCommand command)
    {
        var result = await Sender.Send(command with { ProcessId = id });
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        var result = await Sender.Send(new DeleteProcessCommand(id));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}
