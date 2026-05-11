using IIoT.HttpApi.Infrastructure;
using IIoT.IdentityService.Queries;
using IIoT.Services.Contracts.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[Authorize(Policy = HttpApiPolicies.RequireAiReadToken)]
[EnableRateLimiting(HttpApiRateLimitPolicies.AiRead)]
[Route("api/v1/ai/identity")]
[ApiController]
[Tags("AI Identity")]
public sealed class AiIdentityController : ApiControllerBase
{
    [HttpGet("users/{cloudUserId:guid}/status")]
    public async Task<IActionResult> GetUserStatus(
        [FromRoute] Guid cloudUserId,
        [FromQuery] string? tenantId = CloudIdentityTenants.Default,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(
            new GetCloudIdentityStatusQuery(cloudUserId, tenantId),
            cancellationToken));
    }
}
