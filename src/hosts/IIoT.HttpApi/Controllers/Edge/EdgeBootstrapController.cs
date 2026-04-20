using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.Bootstrap.Devices;
using IIoT.ProductionService.Queries.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[AllowAnonymous]
[Route("api/v1/edge/bootstrap")]
[ApiController]
[Tags("Edge Bootstrap")]
public class EdgeBootstrapController : ApiControllerBase
{
    [HttpGet("device-instance")]
    [EnableRateLimiting("bootstrap")]
    // Keep the legacy clientCode query parameter until deprecated bootstrap callers are retired.
    public async Task<IActionResult> GetDeviceByInstance([FromQuery] string clientCode)
    {
        var result = await Sender.Send(new GetDeviceByInstanceQuery(clientCode));
        if (result.IsSuccess && result.Value is not null)
        {
            RefreshTokenHeaderNames.ApplyTo(
                Response,
                result.Value.RefreshToken,
                result.Value.RefreshTokenExpiresAtUtc,
                result.Value.DeviceIdentity.UploadAccessTokenExpiresAtUtc);
        }

        return ReturnBodyResult(result, session => session.DeviceIdentity);
    }

    [HttpPost("edge-refresh")]
    [EnableRateLimiting("bootstrap")]
    public async Task<IActionResult> Refresh(
        [FromHeader(Name = RefreshTokenHeaderNames.RefreshToken)] string refreshToken)
    {
        var result = await Sender.Send(new RefreshEdgeDeviceIdentityCommand(refreshToken));
        if (result.IsSuccess && result.Value is not null)
        {
            RefreshTokenHeaderNames.ApplyTo(
                Response,
                result.Value.RefreshToken,
                result.Value.RefreshTokenExpiresAtUtc,
                result.Value.DeviceIdentity.UploadAccessTokenExpiresAtUtc);
        }

        return ReturnBodyResult(result, session => session.DeviceIdentity);
    }
}
