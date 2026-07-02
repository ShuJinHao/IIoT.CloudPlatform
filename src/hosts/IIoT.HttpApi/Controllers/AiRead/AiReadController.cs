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

    [HttpGet("processes")]
    public async Task<IActionResult> GetProcesses(
        [FromQuery] string? keyword = null,
        [FromQuery] int? maxRows = null,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(new GetAiReadProcessesQuery(keyword, maxRows), cancellationToken));
    }

    [HttpGet("client-releases")]
    public async Task<IActionResult> GetClientReleases(
        [FromQuery] string? channel = null,
        [FromQuery] string? targetRuntime = null,
        [FromQuery] string? status = null,
        [FromQuery] bool includeArchived = false,
        [FromQuery] int? maxRows = null,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(
            new GetAiReadClientReleaseVersionsQuery(channel, targetRuntime, status, includeArchived, maxRows),
            cancellationToken));
    }

    [HttpGet("device-client-states")]
    public async Task<IActionResult> GetDeviceClientStates(
        [FromQuery] Guid? deviceId = null,
        [FromQuery] string? keyword = null,
        [FromQuery] int? maxRows = null,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(
            new GetAiReadDeviceClientStatesQuery(deviceId, keyword, maxRows),
            cancellationToken));
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
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null,
        [FromQuery] string? level = null,
        [FromQuery] string? keyword = null,
        [FromQuery] string? preset = null,
        [FromQuery] string? minLevel = null,
        [FromQuery] int? maxRows = null,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(
            new GetAiReadDeviceLogsQuery(deviceId, startTime, endTime, level, keyword, preset, minLevel, maxRows),
            cancellationToken));
    }

    [HttpGet("capacity/hourly")]
    public async Task<IActionResult> GetCapacityHourly(
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly? date = null,
        [FromQuery] string? preset = null,
        [FromQuery] string? plcName = null,
        [FromQuery] int? maxRows = null,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(
            new GetAiReadCapacityHourlyQuery(deviceId, date, preset, plcName, maxRows),
            cancellationToken));
    }

    [HttpGet("production-records")]
    public async Task<IActionResult> GetProductionRecords(
        [FromQuery] string? typeKey = null,
        [FromQuery] Guid? processId = null,
        [FromQuery] Guid? deviceId = null,
        [FromQuery] string? barcode = null,
        [FromQuery] string? result = null,
        [FromQuery] DateTime? startTime = null,
        [FromQuery] DateTime? endTime = null,
        [FromQuery] string? preset = null,
        [FromQuery] string? fieldMode = null,
        [FromQuery] int? maxRows = null,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(
            new GetAiReadProductionRecordsQuery(
                typeKey,
                processId,
                deviceId,
                barcode,
                result,
                startTime,
                endTime,
                preset,
                fieldMode,
                maxRows),
            cancellationToken));
    }
}
