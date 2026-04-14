using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.PassStations;
using IIoT.ProductionService.Queries.PassStations;
using IIoT.Services.Common.Contracts.RecordQueries;
using IIoT.SharedKernel.Paging;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

[Route("api/v1/[controller]")]
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Tags("过站数据 - 生产数据接收与查询")]
public class PassStationController : ApiControllerBase
{
    [HttpPost("injection/batch")]
    public async Task<IActionResult> ReceiveInjectionBatch(
        [FromBody] ReceiveInjectionPassCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpGet("injection/by-barcode-process")]
    public async Task<IActionResult> GetByBarcodeAndProcess(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid processId,
        [FromQuery] string barcode)
    {
        var query = new GetPassStationListQuery<InjectionPassListItemDto>(
            pagination, ProcessId: processId, Barcode: barcode);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpGet("injection/by-time-process")]
    public async Task<IActionResult> GetByTimeAndProcess(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid processId,
        [FromQuery] DateTime startTime,
        [FromQuery] DateTime endTime)
    {
        var query = new GetPassStationListQuery<InjectionPassListItemDto>(
            pagination, ProcessId: processId, StartTime: startTime, EndTime: endTime);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpGet("injection/by-device-barcode")]
    public async Task<IActionResult> GetByDeviceAndBarcode(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] string barcode)
    {
        var query = new GetPassStationListQuery<InjectionPassListItemDto>(
            pagination, DeviceId: deviceId, Barcode: barcode);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpGet("injection/by-device-time")]
    public async Task<IActionResult> GetByDeviceAndTime(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] DateTime startTime,
        [FromQuery] DateTime endTime)
    {
        var query = new GetPassStationListQuery<InjectionPassListItemDto>(
            pagination, DeviceId: deviceId, StartTime: startTime, EndTime: endTime);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpGet("injection/{id}")]
    public async Task<IActionResult> GetInjectionDetail([FromRoute] Guid id)
    {
        var query = new GetPassStationDetailQuery<InjectionPassDetailDto>(id);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpGet("injection/device/{deviceId}/latest")]
    public async Task<IActionResult> GetInjectionLatest200ByDevice(
        [FromRoute] Guid deviceId,
        [FromQuery] Pagination pagination)
    {
        var query = new GetPassStationLatest200Query<InjectionPassListItemDto>(deviceId, pagination);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}
