using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands;
using IIoT.ProductionService.Commands.PassStations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[Authorize(Policy = HttpApiPolicies.RequireEdgeDeviceToken)]
[Route("api/v1/edge/pass-stations")]
[ApiController]
[Tags("Edge Pass Stations")]
public class EdgePassStationController : ApiControllerBase
{
    [HttpPost("{typeKey}/batch")]
    [EnableRateLimiting(HttpApiRateLimitPolicies.PassStationUpload)]
    [RequestSizeLimit(UploadValidationLimits.MaxUploadRequestBodyBytes)]
    public async Task<IActionResult> ReceiveBatch(
        [FromRoute] string typeKey,
        [FromBody] PassStationBatchUploadRequest request,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(
            new ReceivePassStationBatchCommand(
                typeKey,
                request.DeviceId,
                request.Items,
                request.RequestId),
            cancellationToken));
    }
}
