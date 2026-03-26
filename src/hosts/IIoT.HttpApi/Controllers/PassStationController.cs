using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.PassStations;
using IIoT.ProductionService.Queries.PassStations.Injection;
using IIoT.SharedKernel.Paging;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

/// <summary>
/// 过站数据科：接收边缘端上报的电芯过站检测数据 + 云端后台追溯查询
/// </summary>
[Route("api/v1/[controller]")]
[ApiController]
[Tags("过站数据科 - 生产数据接收与查询")]
public class PassStationController : ApiControllerBase
{
    // ==========================================
    // 写入接口（边缘端上报）
    // ==========================================

    /// <summary>
    /// 接收单条注液工序过站数据
    /// </summary>
    [HttpPost("injection")]
    public async Task<IActionResult> ReceiveInjection([FromBody] ReceiveInjectionPassCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 批量接收注液工序过站数据（边缘端补传用）
    /// </summary>
    [HttpPost("injection/batch")]
    public async Task<IActionResult> ReceiveInjectionBatch([FromBody] List<ReceiveInjectionPassCommand> commands)
    {
        foreach (var command in commands)
        {
            var result = await Sender.Send(command);
            if (!result.IsSuccess) return BadRequest(result.Errors);
        }

        return Ok(new { message = $"已接收 {commands.Count} 条注液过站数据" });
    }

    // ==========================================
    // 查询接口（云端后台追溯）
    // ==========================================

    /// <summary>
    /// 追溯查询一：条码 + 工序精确查询
    /// </summary>
    [HttpGet("injection/by-barcode-process")]
    public async Task<IActionResult> GetByBarcodeAndProcess(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid processId,
        [FromQuery] string barcode)
    {
        var query = new GetInjectionByBarcodeAndProcessQuery(pagination, processId, barcode);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 追溯查询二：时间范围 + 工序精确查询
    /// </summary>
    [HttpGet("injection/by-time-process")]
    public async Task<IActionResult> GetByTimeAndProcess(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid processId,
        [FromQuery] DateTime startTime,
        [FromQuery] DateTime endTime)
    {
        var query = new GetInjectionByTimeAndProcessQuery(pagination, processId, startTime, endTime);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 追溯查询三：设备号 + 条码精确查询
    /// </summary>
    [HttpGet("injection/by-device-barcode")]
    public async Task<IActionResult> GetByDeviceAndBarcode(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] string barcode)
    {
        var query = new GetInjectionByDeviceAndBarcodeQuery(pagination, deviceId, barcode);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 追溯查询四：设备号 + 时间范围精确查询
    /// </summary>
    [HttpGet("injection/by-device-time")]
    public async Task<IActionResult> GetByDeviceAndTime(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] DateTime startTime,
        [FromQuery] DateTime endTime)
    {
        var query = new GetInjectionByDeviceAndTimeQuery(pagination, deviceId, startTime, endTime);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 详情查询：根据 ID 获取单条注液过站数据（含全部字段）
    /// </summary>
    [HttpGet("injection/{id}")]
    public async Task<IActionResult> GetInjectionDetail([FromRoute] Guid id)
    {
        var query = new GetInjectionDetailQuery(id);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}