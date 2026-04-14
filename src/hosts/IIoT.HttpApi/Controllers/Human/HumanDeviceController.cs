using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.Devices;
using IIoT.ProductionService.Queries.Devices;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

[Route("api/v1/human/devices")]
[ApiController]
[Tags("Human Devices")]
public class HumanDeviceController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPagedList([FromQuery] GetMyDevicesPagedQuery query)
    {
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpGet("all")]
    public async Task<IActionResult> GetAll()
    {
        var result = await Sender.Send(new GetAllDevicesQuery());
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpPost]
    public async Task<IActionResult> Register([FromBody] RegisterDeviceCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateDeviceProfileCommand command)
    {
        var result = await Sender.Send(command with { DeviceId = id });
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        var result = await Sender.Send(new DeleteDeviceCommand(id));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}
