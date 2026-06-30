using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.ClientVersions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

[Authorize(Policy = HttpApiPolicies.RequireEdgeDeviceToken)]
[Route("api/v1/edge/runtime-heartbeats")]
[ApiController]
[Tags("Edge Runtime Heartbeats")]
public sealed class EdgeRuntimeHeartbeatController : ApiControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report(
        [FromBody] ReportDeviceRuntimeHeartbeatCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(command with
        {
            RemoteIpAddress = ResolveRemoteIpAddress()
        }, cancellationToken));
    }

    private string? ResolveRemoteIpAddress()
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor)
            && forwardedFor.Count > 0)
        {
            var firstForwarded = forwardedFor[0]?
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstForwarded))
            {
                remoteIp = firstForwarded;
            }
        }

        return remoteIp;
    }
}
