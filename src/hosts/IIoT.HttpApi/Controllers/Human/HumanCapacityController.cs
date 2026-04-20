using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Queries.Capacities;
using IIoT.SharedKernel.Paging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

[Authorize]
[Route("api/v1/human/capacity")]
[ApiController]
[Tags("Human Capacity")]
public class HumanCapacityController : ApiControllerBase
{
    [HttpGet("hourly")]
    public async Task<IActionResult> GetHourly(
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly date,
        [FromQuery] string? plcName = null)
    {
        return ReturnResult(await Sender.Send(new GetHourlyByDeviceIdQuery(deviceId, date, plcName)));
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly date,
        [FromQuery] string? plcName = null)
    {
        return ReturnResult(await Sender.Send(new GetSummaryByDeviceIdQuery(deviceId, date, plcName)));
    }

    [HttpGet("summary/range")]
    public async Task<IActionResult> GetSummaryRange(
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate,
        [FromQuery] string? plcName = null)
    {
        return ReturnResult(await Sender.Send(new GetSummaryRangeQuery(deviceId, startDate, endDate, plcName)));
    }

    [HttpGet("daily")]
    public async Task<IActionResult> GetDailyPaged(
        [FromQuery] Pagination pagination,
        [FromQuery] DateOnly? date = null,
        [FromQuery] Guid? deviceId = null)
    {
        return ReturnResult(await Sender.Send(new GetDailyCapacityPagedQuery(pagination, date, deviceId)));
    }
}
