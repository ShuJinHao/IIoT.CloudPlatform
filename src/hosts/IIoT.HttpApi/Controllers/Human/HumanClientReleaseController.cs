using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.ClientReleases;
using IIoT.ProductionService.ClientReleases;
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
        [FromQuery] bool includeArchived,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(
            new GetClientReleaseCatalogQuery(channel, targetRuntime, onlyPublished, includeArchived),
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

    [HttpGet("retention-policy")]
    public async Task<IActionResult> GetRetentionPolicy(CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(
            new GetClientReleaseRetentionPolicyQuery(),
            cancellationToken));
    }

    [HttpPut("retention-policy")]
    public async Task<IActionResult> UpdateRetentionPolicy(
        [FromBody] UpdateClientReleaseRetentionPolicyCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(command, cancellationToken));
    }

    [HttpDelete("{releaseId:guid}")]
    public async Task<IActionResult> ArchiveRelease(
        [FromRoute] Guid releaseId,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(
            new ArchiveClientReleaseCommand(releaseId),
            cancellationToken));
    }

    [HttpDelete("{releaseId:guid}/package")]
    public async Task<IActionResult> DeleteReleasePackage(
        [FromRoute] Guid releaseId,
        [FromBody] DeleteClientReleasePackageRequest? request,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(
            new DeleteClientReleasePackageCommand(releaseId, request?.Reason),
            cancellationToken));
    }

    [HttpDelete("components/{componentId:guid}")]
    public async Task<IActionResult> HardDeleteComponent(
        [FromRoute] Guid componentId,
        [FromBody] HardDeleteClientReleaseComponentRequest? request,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(
            new HardDeleteClientReleaseComponentCommand(componentId, request?.Reason),
            cancellationToken));
    }

    [HttpPut("{releaseId:guid}/status")]
    public async Task<IActionResult> UpdateReleaseStatus(
        [FromRoute] Guid releaseId,
        [FromBody] UpdateClientReleaseStatusRequest request,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(
            new UpdateClientReleaseStatusCommand(releaseId, request.Status),
            cancellationToken));
    }

    [HttpPost("installer-package")]
    public async Task<IActionResult> GenerateInstallerPackage(
        [FromBody] GenerateEdgeInstallerPackageCommand command,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return ReturnResult(result);
        }

        Response.Headers.CacheControl = "no-store";
        return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
    }

    [HttpPost("edge-release-bundles")]
    [RequestSizeLimit(EdgeReleaseUploadOptions.DefaultMaxBundleBytes)]
    public async Task<IActionResult> PublishEdgeReleaseBundle(CancellationToken cancellationToken)
    {
        var result = await Sender.Send(
            new PublishEdgeReleaseBundleCommand(),
            cancellationToken);
        return ReturnResult(result);
    }

    [HttpPost("plugin-packages")]
    [RequestSizeLimit(EdgeReleaseUploadOptions.DefaultMaxBundleBytes)]
    public async Task<IActionResult> PublishEdgePluginPackage(CancellationToken cancellationToken)
    {
        var result = await Sender.Send(
            new PublishEdgePluginPackageCommand(),
            cancellationToken);
        return ReturnResult(result);
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

public sealed record UpdateClientReleaseStatusRequest(string Status);

public sealed record DeleteClientReleasePackageRequest(string? Reason);

public sealed record HardDeleteClientReleaseComponentRequest(string? Reason);
