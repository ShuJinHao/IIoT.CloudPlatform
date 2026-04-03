using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.Capacities;
using IIoT.ProductionService.Queries.Capacities;
using IIoT.SharedKernel.Paging;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

/// <summary>
/// 产能模块：接收 Edge 端产能上传 + 提供产能查询
/// 统一使用 deviceId（Guid）作为唯一标识
/// </summary>
[Route("api/v1/[controller]")]
[ApiController]
[Tags("产能 - 产能上传与查询")]
public class CapacityController : ApiControllerBase
{
    // ==========================================
    // 写入接口（Edge 端上报）
    // ==========================================

    /// <summary>
    /// 接收半小时产能上报（实时同步 + 离线补传统一入口）
    /// </summary>
    [HttpPost("hourly")]
    public async Task<IActionResult> ReceiveHourly([FromBody] ReceiveHourlyCapacityCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    // ==========================================
    // 查询接口（统一用 deviceId）
    // ==========================================

    /// <summary>
    /// 按日查询半小时明细（日查询优先调用）
    /// GET /api/v1/Capacity/hourly?deviceId=xxx&date=2026-04-01
    /// </summary>
    [HttpGet("hourly")]
    public async Task<IActionResult> GetHourly(
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly date)
    {
        var query = new GetHourlyByDeviceIdQuery(deviceId, date);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 按日查询汇总（日查询兜底）
    /// GET /api/v1/Capacity/summary?deviceId=xxx&date=2026-04-01
    /// 无数据返回 204
    /// </summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly date)
    {
        var query = new GetSummaryByDeviceIdQuery(deviceId, date);
        var result = await Sender.Send(query);

        if (!result.IsSuccess)
            return BadRequest(result.Errors);

        return result.Value is null ? NoContent() : Ok(result.Value);
    }

    /// <summary>
    /// 按日期范围查询每日汇总（月/年查询使用，一次请求搞定）
    /// GET /api/v1/Capacity/summary/range?deviceId=xxx&startDate=2026-03-01&endDate=2026-03-31
    /// </summary>
    [HttpGet("summary/range")]
    public async Task<IActionResult> GetSummaryRange(
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate)
    {
        var query = new GetSummaryRangeQuery(deviceId, startDate, endDate);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    // ==========================================
    // 云端后台管理
    // ==========================================

    /// <summary>
    /// 所有机台产能分页查询（从 hourly_capacity 聚合为日粒度）
    /// </summary>
    [HttpGet("daily")]
    public async Task<IActionResult> GetDailyPaged(
        [FromQuery] Pagination pagination,
        [FromQuery] DateOnly? date = null,
        [FromQuery] Guid? deviceId = null)
    {
        var query = new GetDailyCapacityPagedQuery(pagination, date, deviceId);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}