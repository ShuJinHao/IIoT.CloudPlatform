using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Queries.DeviceLogs;
using IIoT.SharedKernel.Paging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

[Authorize]
[Route("api/v1/human/device-logs")]
[ApiController]
[Tags("Human Device Logs")]
public class HumanDeviceLogController : ApiControllerBase
{
    [HttpGet("by-level")]
    public async Task<IActionResult> GetByLevel(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] string? level = null)
    {
        return ReturnResult(await Sender.Send(new GetDeviceLogsQuery(pagination, deviceId, Level: level)));
    }

    [HttpGet("by-keyword")]
    public async Task<IActionResult> GetByKeyword(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] string keyword)
    {
        return ReturnResult(await Sender.Send(new GetDeviceLogsQuery(pagination, deviceId, Keyword: keyword)));
    }

    [HttpGet("by-date")]
    public async Task<IActionResult> GetByDate(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly date)
    {
        var start = date.ToDateTime(TimeOnly.MinValue);
        var end = date.ToDateTime(TimeOnly.MaxValue);
        return ReturnResult(await Sender.Send(new GetDeviceLogsQuery(pagination, deviceId, StartTime: start, EndTime: end)));
    }

    [HttpGet("by-time-range")]
    public async Task<IActionResult> GetByTimeRange(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] DateTime startTime,
        [FromQuery] DateTime endTime)
    {
        return ReturnResult(await Sender.Send(new GetDeviceLogsQuery(pagination, deviceId, StartTime: startTime, EndTime: endTime)));
    }

    [HttpGet("by-date-keyword")]
    public async Task<IActionResult> GetByDateAndKeyword(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly date,
        [FromQuery] string keyword)
    {
        var start = date.ToDateTime(TimeOnly.MinValue);
        var end = date.ToDateTime(TimeOnly.MaxValue);
        return ReturnResult(await Sender.Send(new GetDeviceLogsQuery(pagination, deviceId, Keyword: keyword, StartTime: start, EndTime: end)));
    }
}
