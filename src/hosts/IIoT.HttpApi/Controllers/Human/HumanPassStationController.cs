using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Queries.PassStations;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Paging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[Authorize]
[EnableRateLimiting(HttpApiRateLimitPolicies.GeneralApi)]
[Route("api/v1/human/pass-stations")]
[ApiController]
[Tags("Human Pass Stations")]
public class HumanPassStationController : ApiControllerBase
{
    [HttpGet("types")]
    public async Task<IActionResult> GetTypes(CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new GetPassStationTypesQuery(), cancellationToken));
    }

    [HttpGet("{typeKey}")]
    public async Task<IActionResult> GetByType(
        [FromRoute] string typeKey,
        [FromQuery] Pagination pagination,
        [FromQuery] string mode,
        [FromQuery] Guid? processId,
        [FromQuery] Guid? deviceId,
        [FromQuery] string? barcode,
        [FromQuery] DateTime? startTime,
        [FromQuery] DateTime? endTime,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(
            new GetPassStationListByTypeQuery(
                new PassStationQueryRequest(
                    Normalize(typeKey),
                    Normalize(mode),
                    pagination,
                    processId,
                    deviceId,
                    barcode?.Trim(),
                    startTime,
                    endTime)),
            cancellationToken));
    }

    [HttpGet("{typeKey}/{id:guid}")]
    public async Task<IActionResult> GetDetail(
        [FromRoute] string typeKey,
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(
            new GetPassStationDetailByTypeQuery(Normalize(typeKey), id),
            cancellationToken));
    }

    private static string Normalize(string value)
    {
        return value?.Trim().ToLowerInvariant() ?? string.Empty;
    }
}
