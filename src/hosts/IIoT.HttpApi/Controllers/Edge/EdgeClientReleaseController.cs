using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.ClientVersions;
using IIoT.ProductionService.Queries.ClientReleases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

[Authorize(Policy = HttpApiPolicies.RequireEdgeDeviceToken)]
[Route("api/v1/edge/client-releases")]
[ApiController]
[Tags("Edge Client Releases")]
public sealed class EdgeClientReleaseController : ApiControllerBase
{
    [HttpGet("device/{deviceId:guid}/catalog")]
    public async Task<IActionResult> GetCatalog(
        [FromRoute] Guid deviceId,
        [FromQuery] string? channel,
        [FromQuery] string? targetRuntime,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(
            new GetEdgeClientReleaseCatalogQuery(deviceId, channel, targetRuntime),
            cancellationToken));
    }

    [HttpPost("version-reports")]
    public async Task<IActionResult> ReportVersion(
        [FromBody] ReportDeviceClientVersionCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(command, cancellationToken));
    }
}
