using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.DeviceLogs;
using IIoT.ProductionService.Queries.DeviceLogs;
using IIoT.SharedKernel.Paging;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

/// <summary>
/// 日志科：接收边缘端设备日志 + 云端后台日志查询
/// </summary>
[Route("api/v1/[controller]")]
[ApiController]
[Tags("日志科 - 设备日志接收与查询")]
public class DeviceLogController : ApiControllerBase
{
    // ==========================================
    // 写入接口（边缘端推送）
    // ==========================================

    /// <summary>
    /// 接收设备日志（支持批量）
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Receive([FromBody] ReceiveDeviceLogCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    // ==========================================
    // 查询接口（云端后台）
    // ==========================================

    /// <summary>
    /// 日志查询一：设备号 + 日志级别筛选
    /// </summary>
    [HttpGet("by-level")]
    public async Task<IActionResult> GetByDeviceAndLevel(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] string? level = null)
    {
        var query = new GetLogsByDeviceAndLevelQuery(pagination, deviceId, level);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 日志查询二：设备号 + 模糊搜索
    /// </summary>
    [HttpGet("by-keyword")]
    public async Task<IActionResult> GetByDeviceAndKeyword(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] string keyword)
    {
        var query = new GetLogsByDeviceAndKeywordQuery(pagination, deviceId, keyword);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 日志查询三：设备号 + 指定日期
    /// </summary>
    [HttpGet("by-date")]
    public async Task<IActionResult> GetByDeviceAndDate(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly date)
    {
        var query = new GetLogsByDeviceAndDateQuery(pagination, deviceId, date);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 日志查询四：设备号 + 时间范围
    /// </summary>
    [HttpGet("by-time-range")]
    public async Task<IActionResult> GetByDeviceAndTimeRange(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] DateTime startTime,
        [FromQuery] DateTime endTime)
    {
        var query = new GetLogsByDeviceAndTimeRangeQuery(pagination, deviceId, startTime, endTime);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 日志查询五：设备号 + 日期 + 模糊搜索（最精确定位）
    /// </summary>
    [HttpGet("by-date-keyword")]
    public async Task<IActionResult> GetByDeviceDateAndKeyword(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly date,
        [FromQuery] string keyword)
    {
        var query = new GetLogsByDeviceDateAndKeywordQuery(pagination, deviceId, date, keyword);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}