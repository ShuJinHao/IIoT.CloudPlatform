using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.Capacities;
using IIoT.ProductionService.Queries.Capacities;
using IIoT.SharedKernel.Paging;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

/// <summary>
/// 产能科：接收边缘端产能汇总 + 云端后台产能查询
/// </summary>
[Route("api/v1/[controller]")]
[ApiController]
[Tags("产能科 - 产能汇总接收与查询")]
public class CapacityController : ApiControllerBase
{
    // ==========================================
    // 写入接口（边缘端上报）
    // ==========================================

    /// <summary>
    /// 接收单日产能汇总
    /// </summary>
    [HttpPost("daily")]
    public async Task<IActionResult> ReceiveDaily([FromBody] ReceiveDailyCapacityCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    // ==========================================
    // 查询接口（云端后台）
    // ==========================================

    /// <summary>
    /// 所有机台产能分页查询（延迟加载，带设备名称和良率）
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

    /// <summary>
    /// 单机台产能汇总查询（带缓存，默认最近7天走缓存）
    /// </summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetDeviceSummary(
        [FromQuery] Guid deviceId,
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate)
    {
        var query = new GetDeviceCapacitySummaryQuery(deviceId, startDate, endDate);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}