using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Queries.Devices;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

[Route("api/v1/edge/bootstrap")]
[ApiController]
[Tags("Edge Bootstrap")]
public class EdgeBootstrapController : ApiControllerBase
{
    [HttpGet("device-instance")]
    public async Task<IActionResult> GetDeviceByInstance(
        [FromQuery] string macAddress,
        [FromQuery] string clientCode)
    {
        var result = await Sender.Send(new GetDeviceByInstanceQuery(macAddress, clientCode));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}
