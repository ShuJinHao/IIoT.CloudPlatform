using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.Devices;
using IIoT.ProductionService.Queries.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

[Authorize]
[Route("api/v1/human/devices")]
[ApiController]
[Tags("Human Devices")]
public class HumanDeviceController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPagedList([FromQuery] GetMyDevicesPagedQuery query)
    {
        return ReturnResult(await Sender.Send(query));
    }

    [HttpGet("all")]
    public async Task<IActionResult> GetAll()
    {
        return ReturnResult(await Sender.Send(new GetAllDevicesQuery()));
    }

    [HttpPost]
    public async Task<IActionResult> Register([FromBody] RegisterDeviceCommand command)
    {
        return ReturnResult(
            await Sender.Send(command),
            result => $"/api/v1/human/devices/{result.Id}");
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateDeviceProfileCommand command)
    {
        return ReturnResult(await Sender.Send(command with { DeviceId = id }));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        return ReturnResult(await Sender.Send(new DeleteDeviceCommand(id)));
    }
}
