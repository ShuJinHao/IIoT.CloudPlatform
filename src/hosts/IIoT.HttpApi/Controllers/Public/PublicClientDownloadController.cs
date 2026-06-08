using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Queries.ClientReleases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[AllowAnonymous]
[EnableRateLimiting(HttpApiRateLimitPolicies.GeneralApi)]
[Route("api/v1/public/client-downloads")]
[ApiController]
[Tags("Public Client Downloads")]
public sealed class PublicClientDownloadController : ApiControllerBase
{
    [HttpGet("latest")]
    public async Task<IActionResult> GetLatest(
        [FromQuery] string? channel,
        [FromQuery] string? targetRuntime,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(
            new GetPublicClientDownloadsQuery(channel, targetRuntime),
            cancellationToken));
    }
}
