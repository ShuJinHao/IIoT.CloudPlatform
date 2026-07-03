using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands;
using IIoT.ProductionService.Commands.EdgeHosts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[Authorize(Policy = HttpApiPolicies.RequireEdgeDeviceToken)]
[Route("api/v1/edge/edge-hosts/plc-runtime-states")]
[ApiController]
[Tags("Edge Host PLC Runtime States")]
public sealed class EdgeHostPlcRuntimeStateController : ApiControllerBase
{
    [HttpPost]
    [EnableRateLimiting(HttpApiRateLimitPolicies.EdgeHostPlcStateUpload)]
    [RequestSizeLimit(UploadValidationLimits.MaxUploadRequestBodyBytes)]
    public async Task<IActionResult> Report(
        [FromBody] ReportEdgeHostPlcRuntimeStatesCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(command, cancellationToken));
    }
}
