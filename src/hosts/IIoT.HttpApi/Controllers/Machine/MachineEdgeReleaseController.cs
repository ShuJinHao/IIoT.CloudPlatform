using IIoT.HttpApi.Infrastructure;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[AllowAnonymous]
[EnableRateLimiting(HttpApiRateLimitPolicies.GeneralApi)]
[Route("api/v1/machine/edge-release")]
[ApiController]
[Tags("Machine Edge Release")]
public sealed class MachineEdgeReleaseController(
    IEdgeReleaseApiKeyService apiKeyService,
    IJwtTokenGenerator jwtTokenGenerator) : ApiControllerBase
{
    private const string ApiKeyHeaderName = "X-IIoT-Edge-Release-Key";

    [HttpPost("token")]
    public async Task<IActionResult> IssueToken(CancellationToken cancellationToken)
    {
        var apiKey = Request.Headers[ApiKeyHeaderName].FirstOrDefault();
        var validation = await apiKeyService.ValidateAsync(apiKey ?? string.Empty, cancellationToken);
        if (!validation.IsSuccess || validation.Value is null)
        {
            return ReturnResult(Result.From(validation));
        }

        var token = jwtTokenGenerator.GenerateEdgeReleasePublisherToken(
            validation.Value.Id,
            validation.Value.Name,
            validation.Value.Permissions);

        return Ok(new EdgeReleaseMachineTokenResponse(
            token.Token,
            token.ExpiresAtUtc,
            validation.Value.Name,
            validation.Value.Permissions));
    }
}

public sealed record EdgeReleaseMachineTokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string Name,
    IReadOnlyList<string> Permissions);
