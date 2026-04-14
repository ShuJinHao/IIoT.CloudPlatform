using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.DeviceLogs;
using IIoT.ProductionService.Queries.DeviceLogs;
using IIoT.SharedKernel.Paging;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

/// <summary>
/// 设备日志接口。
/// </summary>
[Route("api/v1/[controller]")]
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Tags("日志科 - 设备日志接收与查询")]
public class DeviceLogController : ApiControllerBase
{
    /// <summary>
    /// 接收设备日志。
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Receive([FromBody] ReceiveDeviceLogCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 按级别查询日志。
    /// </summary>
    [HttpGet("by-level")]
    public async Task<IActionResult> GetByDeviceAndLevel(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] string? level = null)
    {
        var query = new GetDeviceLogsQuery(pagination, deviceId, Level: level);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 按关键字查询日志。
    /// </summary>
    [HttpGet("by-keyword")]
    public async Task<IActionResult> GetByDeviceAndKeyword(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] string keyword)
    {
        var query = new GetDeviceLogsQuery(pagination, deviceId, Keyword: keyword);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 按日期查询日志。
    /// </summary>
    [HttpGet("by-date")]
    public async Task<IActionResult> GetByDeviceAndDate(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly date)
    {
        var start = date.ToDateTime(TimeOnly.MinValue);
        var end   = date.ToDateTime(TimeOnly.MaxValue);
        var query = new GetDeviceLogsQuery(pagination, deviceId,
            StartTime: start, EndTime: end);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 按时间范围查询日志。
    /// </summary>
    [HttpGet("by-time-range")]
    public async Task<IActionResult> GetByDeviceAndTimeRange(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] DateTime startTime,
        [FromQuery] DateTime endTime)
    {
        var query = new GetDeviceLogsQuery(pagination, deviceId,
            StartTime: startTime, EndTime: endTime);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 按日期和关键字查询日志。
    /// </summary>
    [HttpGet("by-date-keyword")]
    public async Task<IActionResult> GetByDeviceDateAndKeyword(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly date,
        [FromQuery] string keyword)
    {
        var start = date.ToDateTime(TimeOnly.MinValue);
        var end   = date.ToDateTime(TimeOnly.MaxValue);
        var query = new GetDeviceLogsQuery(pagination, deviceId,
            Keyword: keyword, StartTime: start, EndTime: end);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}
