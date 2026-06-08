using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.ClientReleases;
using IIoT.ProductionService.Queries.ClientReleases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[Authorize]
[EnableRateLimiting(HttpApiRateLimitPolicies.GeneralApi)]
[Route("api/v1/human/client-releases")]
[ApiController]
[Tags("Human Client Releases")]
public sealed class HumanClientReleaseController : ApiControllerBase
{
    [HttpGet("catalog")]
    public async Task<IActionResult> GetCatalog(
        [FromQuery] string? channel,
        [FromQuery] string? targetRuntime,
        [FromQuery] bool onlyPublished,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(
            new GetClientReleaseCatalogQuery(channel, targetRuntime, onlyPublished),
            cancellationToken));
    }

    [HttpGet("device-inventory")]
    public async Task<IActionResult> GetDeviceInventory(
        [FromQuery] string? channel,
        [FromQuery] string? targetRuntime,
        [FromQuery] string? keyword,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(
            new GetDeviceClientVersionInventoryQuery(channel, targetRuntime, keyword),
            cancellationToken));
    }

    [HttpPost("host-releases")]
    public async Task<IActionResult> UpsertHostRelease(
        [FromBody] UpsertClientHostReleaseCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(
            await Sender.Send(command, cancellationToken),
            result => $"/api/v1/human/client-releases/host-releases/{result.Id}");
    }

    [HttpPost("plugin-releases")]
    public async Task<IActionResult> UpsertPluginRelease(
        [FromBody] UpsertClientPluginReleaseCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(
            await Sender.Send(command, cancellationToken),
            result => $"/api/v1/human/client-releases/plugin-releases/{result.Id}");
    }
}
