using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Queries.Capacities;
using IIoT.SharedKernel.Paging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[Authorize]
[EnableRateLimiting(HttpApiRateLimitPolicies.GeneralApi)]
[Route("api/v1/human/capacity")]
[ApiController]
[Tags("Human Capacity")]
public class HumanCapacityController : ApiControllerBase
{
    [HttpGet("hourly")]
    public async Task<IActionResult> GetHourly(
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly? date = null,
        [FromQuery] string? plcName = null,
        [FromQuery] string? plcCode = null,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(new GetHourlyByDeviceIdQuery(deviceId, date, plcName, plcCode), cancellationToken));
    }

    [HttpGet("hourly/aggregate")]
    public async Task<IActionResult> GetHourlyAggregate(
        [FromQuery] DateOnly? date = null,
        [FromQuery] Guid? processId = null,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(new GetHourlyCapacityAggregateQuery(date, processId), cancellationToken));
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly date,
        [FromQuery] string? plcName = null,
        [FromQuery] string? plcCode = null,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(new GetSummaryByDeviceIdQuery(deviceId, date, plcName, plcCode), cancellationToken));
    }

    [HttpGet("summary/range")]
    public async Task<IActionResult> GetSummaryRange(
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate,
        [FromQuery] string? plcName = null,
        [FromQuery] string? plcCode = null,
        [FromQuery] bool breakdownByPlc = false,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(
            new GetSummaryRangeQuery(deviceId, startDate, endDate, plcName, breakdownByPlc, plcCode),
            cancellationToken));
    }

    [HttpGet("daily")]
    public async Task<IActionResult> GetDailyPaged(
        [FromQuery] Pagination pagination,
        [FromQuery] DateOnly? date = null,
        [FromQuery] Guid? deviceId = null,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(new GetDailyCapacityPagedQuery(pagination, date, deviceId), cancellationToken));
    }
}
