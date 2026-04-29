using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands;
using IIoT.ProductionService.Commands.DeviceLogs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[Authorize(Policy = HttpApiPolicies.RequireEdgeDeviceToken)]
[Route("api/v1/edge/device-logs")]
[ApiController]
[Tags("Edge Device Logs")]
public class EdgeDeviceLogController : ApiControllerBase
{
    [HttpPost]
    [EnableRateLimiting(HttpApiRateLimitPolicies.DeviceLogUpload)]
    [RequestSizeLimit(UploadValidationLimits.MaxUploadRequestBodyBytes)]
    public async Task<IActionResult> Receive(
        [FromBody] ReceiveDeviceLogCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(command, cancellationToken));
    }
}
