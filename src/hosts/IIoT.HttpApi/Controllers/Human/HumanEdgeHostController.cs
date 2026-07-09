using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Queries.EdgeHosts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[Authorize]
[EnableRateLimiting(HttpApiRateLimitPolicies.GeneralApi)]
[Route("api/v1/human/edge-hosts")]
[ApiController]
[Tags("Human Edge Hosts")]
public sealed class HumanEdgeHostController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPagedList(
        [FromQuery] GetEdgeHostPagedListQuery query,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(query, cancellationToken));
    }

    [HttpGet("{deviceId:guid}")]
    public async Task<IActionResult> GetDetail(
        [FromRoute] Guid deviceId,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new GetEdgeHostDetailQuery(deviceId), cancellationToken));
    }

    [HttpGet("{deviceId:guid}/plc-runtime-states")]
    public async Task<IActionResult> GetPlcRuntimeStates(
        [FromRoute] Guid deviceId,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new GetEdgeHostPlcRuntimeStatesQuery(deviceId), cancellationToken));
    }
}
