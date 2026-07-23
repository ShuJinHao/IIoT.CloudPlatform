using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Queries.DeviceClientOverviews;
using IIoT.SharedKernel.Paging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[Authorize]
[EnableRateLimiting(HttpApiRateLimitPolicies.GeneralApi)]
[Route("api/v1/human/device-client-overviews")]
[ApiController]
[Tags("Human Device Client Overviews")]
public sealed class HumanDeviceClientOverviewController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPagedList(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? keyword = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(
            new GetDeviceClientOverviewQuery(
                new Pagination { PageNumber = pageNumber, PageSize = pageSize },
                keyword,
                sortBy,
                sortDirection),
            cancellationToken));
    }

    [HttpGet("{deviceId:guid}/release-details")]
    public async Task<IActionResult> GetReleaseDetails(
        [FromRoute] Guid deviceId,
        [FromQuery] string? channel = null,
        [FromQuery] string? targetRuntime = null,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(
            new GetDeviceClientReleaseDetailQuery(deviceId, channel, targetRuntime),
            cancellationToken));
    }
}
