using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Queries.Recipes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

[Authorize]
[Route("api/v1/edge/recipes")]
[ApiController]
[Tags("Edge Recipes")]
public class EdgeRecipeController : ApiControllerBase
{
    [HttpGet("device/{deviceId}")]
    public async Task<IActionResult> GetByDeviceId([FromRoute] Guid deviceId)
    {
        var result = await Sender.Send(new GetRecipesByDeviceIdQuery(deviceId));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}
