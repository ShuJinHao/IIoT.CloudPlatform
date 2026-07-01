using IIoT.HttpApi.Infrastructure;
using IIoT.IdentityService.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[Authorize]
[EnableRateLimiting(HttpApiRateLimitPolicies.GeneralApi)]
[Route("api/v1/human/client-release-api-keys")]
[ApiController]
[Tags("Human Client Release API Keys")]
public sealed class HumanEdgeReleaseApiKeyController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetList(CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new GetEdgeReleaseApiKeysQuery(), cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateEdgeReleaseApiKeyCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(
            await Sender.Send(command, cancellationToken),
            result => $"/api/v1/human/client-release-api-keys/{result.Id}");
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(
        [FromRoute] Guid id,
        [FromBody] RevokeEdgeReleaseApiKeyRequest? request,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(
            new RevokeEdgeReleaseApiKeyCommand(id, request?.Reason),
            cancellationToken));
    }
}

public sealed record RevokeEdgeReleaseApiKeyRequest(string? Reason);
