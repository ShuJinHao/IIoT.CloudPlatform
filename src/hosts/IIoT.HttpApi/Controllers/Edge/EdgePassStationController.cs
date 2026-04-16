using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.PassStations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

[Authorize]
[Route("api/v1/edge/pass-stations")]
[ApiController]
[Tags("Edge Pass Stations")]
public class EdgePassStationController : ApiControllerBase
{
    [HttpPost("injection/batch")]
    public async Task<IActionResult> ReceiveInjectionBatch([FromBody] ReceiveInjectionPassCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}
