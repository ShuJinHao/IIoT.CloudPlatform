using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.PassStations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[Authorize(Policy = HttpApiPolicies.RequireEdgeDeviceToken)]
[Route("api/v1/edge/pass-stations")]
[ApiController]
[Tags("Edge Pass Stations")]
public class EdgePassStationController : ApiControllerBase
{
    [HttpPost("injection/batch")]
    [EnableRateLimiting("edge-upload")]
    public async Task<IActionResult> ReceiveInjectionBatch([FromBody] ReceiveInjectionPassCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpPost("stacking")]
    [EnableRateLimiting("edge-upload")]
    public async Task<IActionResult> ReceiveStacking([FromBody] ReceiveStackingPassCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}
