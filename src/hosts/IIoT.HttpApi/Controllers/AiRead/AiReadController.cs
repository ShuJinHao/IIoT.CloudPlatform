using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Queries.AiRead;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[Authorize(Policy = HttpApiPolicies.RequireAiReadToken)]
[EnableRateLimiting(HttpApiRateLimitPolicies.AiRead)]
[Route("api/v1/ai/read")]
[ApiController]
[Tags("AI Read")]
public sealed class AiReadController : ApiControllerBase
{
    [HttpGet("devices")]
    public async Task<IActionResult> GetDevices(
        [FromQuery] string? keyword = null,
        [FromQuery] int? maxRows = null,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(new GetAiReadDevicesQuery(keyword, maxRows), cancellationToken));
    }

    [HttpGet("capacity/summary")]
    public async Task<IActionResult> GetCapacitySummary(
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate,
        [FromQuery] string? plcName = null,
        [FromQuery] int? maxRows = null,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(
            new GetAiReadCapacitySummaryQuery(deviceId, startDate, endDate, plcName, maxRows),
            cancellationToken));
    }

    [HttpGet("device-logs")]
    public async Task<IActionResult> GetDeviceLogs(
        [FromQuery] Guid deviceId,
        [FromQuery] DateTime? startTime,
        [FromQuery] DateTime? endTime,
        [FromQuery] string? level = null,
        [FromQuery] string? keyword = null,
        [FromQuery] int? maxRows = null,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(
            new GetAiReadDeviceLogsQuery(deviceId, startTime, endTime, level, keyword, maxRows),
            cancellationToken));
    }

    [HttpGet("pass-stations/{typeKey}")]
    public async Task<IActionResult> GetPassStations(
        [FromRoute] string typeKey,
        [FromQuery] DateTime? startTime,
        [FromQuery] DateTime? endTime,
        [FromQuery] Guid? deviceId = null,
        [FromQuery] string? barcode = null,
        [FromQuery] int? maxRows = null,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(
            new GetAiReadPassStationsQuery(typeKey, startTime, endTime, deviceId, barcode, maxRows),
            cancellationToken));
    }
}
