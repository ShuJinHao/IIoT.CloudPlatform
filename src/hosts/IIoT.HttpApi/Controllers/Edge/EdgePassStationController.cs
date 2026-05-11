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
    [HttpPost("/api/v1/edge/process-records")]
    [EnableRateLimiting(HttpApiRateLimitPolicies.PassStationUpload)]
    [RequestSizeLimit(UploadValidationLimits.MaxUploadRequestBodyBytes)]
    public async Task<IActionResult> ReceiveProcessRecords(
        [FromBody] ProcessRecordUploadRequest? request,
        CancellationToken cancellationToken)
    {
        var command = ProcessRecordUploadRequestMapper.ToPassStationCommand(request);
        if (!command.IsSuccess)
        {
            return ReturnResult(command);
        }

        return ReturnResult(await Sender.Send(command.Value!, cancellationToken));
    }

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
                request.RequestId,
                request.SchemaVersion,
                request.ProcessType),
            cancellationToken));
    }
}
