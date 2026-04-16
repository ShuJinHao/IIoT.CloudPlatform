using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Queries.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

[AllowAnonymous]
[Route("api/v1/edge/bootstrap")]
[ApiController]
[Tags("Edge Bootstrap")]
public class EdgeBootstrapController : ApiControllerBase
{
    [HttpGet("device-instance")]
    public async Task<IActionResult> GetDeviceByInstance(
        // 为兼容现有边缘端，暂时保留 legacy 查询参数名。
        [FromQuery] string clientCode)
    {
        var result = await Sender.Send(new GetDeviceByInstanceQuery(clientCode));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}
