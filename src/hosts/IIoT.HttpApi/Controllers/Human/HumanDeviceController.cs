using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.Devices;
using IIoT.ProductionService.Queries.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[Authorize]
[EnableRateLimiting(HttpApiRateLimitPolicies.GeneralApi)]
[Route("api/v1/human/devices")]
[ApiController]
[Tags("Human Devices")]
public class HumanDeviceController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPagedList(
        [FromQuery] GetMyDevicesPagedQuery query,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(query, cancellationToken));
    }

    [HttpGet("all")]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new GetAllDevicesQuery(), cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Register(
        [FromBody] RegisterDeviceCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(
            await Sender.Send(command, cancellationToken),
            result => $"/api/v1/human/devices/{result.Id}");
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(
        [FromRoute] Guid id,
        [FromBody] UpdateDeviceProfileCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(command with { DeviceId = id }, cancellationToken));
    }

    [HttpPost("{id}/bootstrap-secret/rotate")]
    public async Task<IActionResult> RotateBootstrapSecret(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new RotateDeviceBootstrapSecretCommand(id), cancellationToken));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new DeleteDeviceCommand(id), cancellationToken));
    }
}
