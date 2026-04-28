using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.Capacities;
using IIoT.ProductionService.Queries.Capacities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[Authorize(Policy = HttpApiPolicies.RequireEdgeDeviceToken)]
[Route("api/v1/edge/capacity")]
[ApiController]
[Tags("Edge Capacity")]
public class EdgeCapacityController : ApiControllerBase
{
    [HttpPost("hourly")]
    [EnableRateLimiting(HttpApiRateLimitPolicies.CapacityUpload)]
    public async Task<IActionResult> ReceiveHourly(
        [FromBody] ReceiveHourlyCapacityCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(command, cancellationToken));
    }

    [HttpGet("hourly")]
    public async Task<IActionResult> GetHourly(
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly date,
        [FromQuery] string? plcName = null,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(new GetEdgeHourlyByDeviceIdQuery(deviceId, date, plcName), cancellationToken));
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly date,
        [FromQuery] string? plcName = null,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(new GetEdgeSummaryByDeviceIdQuery(deviceId, date, plcName), cancellationToken));
    }

    [HttpGet("summary/range")]
    public async Task<IActionResult> GetSummaryRange(
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate,
        [FromQuery] string? plcName = null,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(new GetEdgeSummaryRangeQuery(deviceId, startDate, endDate, plcName), cancellationToken));
    }
}
