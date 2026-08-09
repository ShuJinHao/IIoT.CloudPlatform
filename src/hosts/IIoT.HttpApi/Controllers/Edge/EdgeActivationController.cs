using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.Bootstrap.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[Authorize(Policy = HttpApiPolicies.RequireEdgeActivationToken)]
[Route("api/v1/edge/bootstrap")]
[ApiController]
[Tags("Edge Activation")]
public sealed class EdgeActivationController : ApiControllerBase
{
    [HttpPost("device-activate")]
    [EnableRateLimiting(HttpApiRateLimitPolicies.Bootstrap)]
    public async Task<IActionResult> Activate(
        [FromBody] ActivateEdgeDeviceCommand command,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken);
        if (result.IsSuccess
            && result.Value?.RefreshToken is not null
            && result.Value.RefreshTokenExpiresAtUtc is not null)
        {
            RefreshTokenResponseFilter.SetHeaders(
                HttpContext,
                result.Value.RefreshToken,
                result.Value.RefreshTokenExpiresAtUtc.Value,
                result.Value.DeviceIdentity.UploadAccessTokenExpiresAtUtc);
        }

        return ReturnBodyResult(result, session => session.DeviceIdentity);
    }
}

[Authorize(Policy = HttpApiPolicies.RequireEdgeDeviceToken)]
[Route("api/v1/edge/bootstrap")]
[ApiController]
[Tags("Edge Activation")]
public sealed class EdgeActivationConfirmationController : ApiControllerBase
{
    [HttpPost("device-activation-confirm")]
    [EnableRateLimiting(HttpApiRateLimitPolicies.Bootstrap)]
    public async Task<IActionResult> Confirm(
        [FromBody] ConfirmEdgeDeviceActivationCommand command,
        CancellationToken cancellationToken)
        => ReturnResult(await Sender.Send(command, cancellationToken));
}
