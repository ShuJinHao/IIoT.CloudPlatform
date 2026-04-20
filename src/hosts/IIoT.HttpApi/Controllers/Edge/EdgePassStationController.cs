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
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPost("stacking")]
    [EnableRateLimiting("edge-upload")]
    public async Task<IActionResult> ReceiveStacking([FromBody] ReceiveStackingPassCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }
}
