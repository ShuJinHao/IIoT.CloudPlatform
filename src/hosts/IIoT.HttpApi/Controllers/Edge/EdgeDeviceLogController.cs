using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.DeviceLogs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[Authorize]
[Route("api/v1/edge/device-logs")]
[ApiController]
[Tags("Edge Device Logs")]
public class EdgeDeviceLogController : ApiControllerBase
{
    [HttpPost]
    [EnableRateLimiting("edge-upload")]
    public async Task<IActionResult> Receive([FromBody] ReceiveDeviceLogCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}
