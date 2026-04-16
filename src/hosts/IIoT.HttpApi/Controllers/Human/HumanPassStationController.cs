using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Queries.PassStations;
using IIoT.Services.Common.Contracts.RecordQueries;
using IIoT.SharedKernel.Paging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

[Authorize]
[Route("api/v1/human/pass-stations")]
[ApiController]
[Tags("Human Pass Stations")]
public class HumanPassStationController : ApiControllerBase
{
    [HttpGet("injection/by-barcode-process")]
    public async Task<IActionResult> GetByBarcodeAndProcess(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid processId,
        [FromQuery] string barcode)
    {
        var result = await Sender.Send(
            new GetPassStationListQuery<InjectionPassListItemDto>(
                pagination, ProcessId: processId, Barcode: barcode));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpGet("injection/by-time-process")]
    public async Task<IActionResult> GetByTimeAndProcess(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid processId,
        [FromQuery] DateTime startTime,
        [FromQuery] DateTime endTime)
    {
        var result = await Sender.Send(
            new GetPassStationListQuery<InjectionPassListItemDto>(
                pagination, ProcessId: processId, StartTime: startTime, EndTime: endTime));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpGet("injection/by-device-barcode")]
    public async Task<IActionResult> GetByDeviceAndBarcode(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] string barcode)
    {
        var result = await Sender.Send(
            new GetPassStationListQuery<InjectionPassListItemDto>(
                pagination, DeviceId: deviceId, Barcode: barcode));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpGet("injection/by-device-time")]
    public async Task<IActionResult> GetByDeviceAndTime(
        [FromQuery] Pagination pagination,
        [FromQuery] Guid deviceId,
        [FromQuery] DateTime startTime,
        [FromQuery] DateTime endTime)
    {
        var result = await Sender.Send(
            new GetPassStationListQuery<InjectionPassListItemDto>(
                pagination, DeviceId: deviceId, StartTime: startTime, EndTime: endTime));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpGet("injection/{id}")]
    public async Task<IActionResult> GetInjectionDetail([FromRoute] Guid id)
    {
        var result = await Sender.Send(new GetPassStationDetailQuery<InjectionPassDetailDto>(id));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpGet("injection/device/{deviceId}/latest")]
    public async Task<IActionResult> GetLatestByDevice(
        [FromRoute] Guid deviceId,
        [FromQuery] Pagination pagination)
    {
        var result = await Sender.Send(
            new GetPassStationLatest200Query<InjectionPassListItemDto>(deviceId, pagination));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}
