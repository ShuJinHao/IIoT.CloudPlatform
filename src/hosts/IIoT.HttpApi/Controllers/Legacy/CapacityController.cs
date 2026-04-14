using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.Capacities;
using IIoT.ProductionService.Queries.Capacities;
using IIoT.SharedKernel.Paging;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

/// <summary>
/// 产能接口。
/// </summary>
[Route("api/v1/[controller]")]
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Tags("产能 - 产能上传与查询")]
public class CapacityController : ApiControllerBase
{
    /// <summary>
    /// 接收半小时产能上报。
    /// </summary>
    [HttpPost("hourly")]
    public async Task<IActionResult> ReceiveHourly([FromBody] ReceiveHourlyCapacityCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 查询半小时明细。
    /// </summary>
    [HttpGet("hourly")]
    public async Task<IActionResult> GetHourly(
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly date,
        [FromQuery] string? plcName = null)
    {
        var result = Request.Headers.Authorization.Count > 0
            ? await Sender.Send(new GetHourlyByDeviceIdQuery(deviceId, date, plcName))
            : await Sender.Send(new GetEdgeHourlyByDeviceIdQuery(deviceId, date, plcName));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 查询日汇总。
    /// </summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly date,
        [FromQuery] string? plcName = null)
    {
        var result = Request.Headers.Authorization.Count > 0
            ? await Sender.Send(new GetSummaryByDeviceIdQuery(deviceId, date, plcName))
            : await Sender.Send(new GetEdgeSummaryByDeviceIdQuery(deviceId, date, plcName));

        if (!result.IsSuccess)
            return BadRequest(result.Errors);

        return result.Value is null ? NoContent() : Ok(result.Value);
    }

    /// <summary>
    /// 查询日期范围汇总。
    /// </summary>
    [HttpGet("summary/range")]
    public async Task<IActionResult> GetSummaryRange(
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate,
        [FromQuery] string? plcName = null)
    {
        var result = Request.Headers.Authorization.Count > 0
            ? await Sender.Send(new GetSummaryRangeQuery(deviceId, startDate, endDate, plcName))
            : await Sender.Send(new GetEdgeSummaryRangeQuery(deviceId, startDate, endDate, plcName));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 查询日产能分页列表。
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
