using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Queries.DeviceLogs;
using IIoT.SharedKernel.Paging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[Authorize]
[EnableRateLimiting(HttpApiRateLimitPolicies.GeneralApi)]
[Route("api/v1/human/device-logs")]
[ApiController]
[Tags("Human Device Logs")]
public class HumanDeviceLogController : ApiControllerBase
{
    [HttpGet("recent")]
    public async Task<IActionResult> GetRecent(
        [FromQuery] int limit = 20,
        [FromQuery] string? minLevel = "WARN",
        [FromQuery] Guid? processId = null,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(new GetRecentDeviceLogsQuery(limit, minLevel, processId), cancellationToken));
    }

    [HttpGet("recent-alerts/count")]
    public async Task<IActionResult> GetRecentAlertCount(
        [FromQuery] Guid? processId = null,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(new GetRecentAlertCountQuery(processId), cancellationToken));
    }

    [HttpGet("by-level")]
    public async Task<IActionResult> GetByLevel(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] string? level = null,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(new GetDeviceLogsQuery(pagination, deviceId, Level: level), cancellationToken));
    }

    [HttpGet("by-keyword")]
    public async Task<IActionResult> GetByKeyword(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] string keyword,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new GetDeviceLogsQuery(pagination, deviceId, Keyword: keyword), cancellationToken));
    }

    [HttpGet("by-date")]
    public async Task<IActionResult> GetByDate(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly date,
        CancellationToken cancellationToken)
    {
        var start = date.ToDateTime(TimeOnly.MinValue);
        var end = date.ToDateTime(TimeOnly.MaxValue);
        return ReturnResult(await Sender.Send(new GetDeviceLogsQuery(pagination, deviceId, StartTime: start, EndTime: end), cancellationToken));
    }

    [HttpGet("by-time-range")]
    public async Task<IActionResult> GetByTimeRange(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] DateTime startTime,
        [FromQuery] DateTime endTime,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new GetDeviceLogsQuery(pagination, deviceId, StartTime: startTime, EndTime: endTime), cancellationToken));
    }

    [HttpGet("by-date-keyword")]
    public async Task<IActionResult> GetByDateAndKeyword(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly date,
        [FromQuery] string keyword,
        CancellationToken cancellationToken)
    {
        var start = date.ToDateTime(TimeOnly.MinValue);
        var end = date.ToDateTime(TimeOnly.MaxValue);
        return ReturnResult(await Sender.Send(new GetDeviceLogsQuery(pagination, deviceId, Keyword: keyword, StartTime: start, EndTime: end), cancellationToken));
    }
}
