using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.EdgeHosts;
using IIoT.ProductionService.Queries.EdgeHosts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[Authorize]
[EnableRateLimiting(HttpApiRateLimitPolicies.GeneralApi)]
[Route("api/v1/human/edge-hosts")]
[ApiController]
[Tags("Human Edge Hosts")]
public sealed class HumanEdgeHostController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPagedList(
        [FromQuery] GetEdgeHostPagedListQuery query,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(query, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetDetail(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new GetEdgeHostDetailQuery(id), cancellationToken));
    }

    [HttpGet("{id:guid}/plc-runtime-states")]
    public async Task<IActionResult> GetPlcRuntimeStates(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new GetEdgeHostPlcRuntimeStatesQuery(id), cancellationToken));
    }

    [HttpGet("{id:guid}/plc-capacity-summary")]
    public async Task<IActionResult> GetPlcCapacitySummary(
        [FromRoute] Guid id,
        [FromQuery] DateOnly date,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new GetEdgeHostPlcCapacitySummaryQuery(id, date), cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateEdgeHostCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(
            await Sender.Send(command, cancellationToken),
            result => $"/api/v1/human/edge-hosts/{result.Id}");
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        [FromRoute] Guid id,
        [FromBody] UpdateEdgeHostCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(command with { EdgeHostId = id }, cancellationToken));
    }

    [HttpPost("{id:guid}/enable")]
    public async Task<IActionResult> Enable(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new EnableEdgeHostCommand(id), cancellationToken));
    }

    [HttpPost("{id:guid}/disable")]
    public async Task<IActionResult> Disable(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new DisableEdgeHostCommand(id), cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new DeleteEdgeHostCommand(id), cancellationToken));
    }

    [HttpPost("{id:guid}/plc-bindings")]
    public async Task<IActionResult> AddPlcBinding(
        [FromRoute] Guid id,
        [FromBody] AddEdgeHostPlcBindingCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(command with { EdgeHostId = id }, cancellationToken));
    }

    [HttpPut("{id:guid}/plc-bindings/{bindingId:guid}")]
    public async Task<IActionResult> UpdatePlcBinding(
        [FromRoute] Guid id,
        [FromRoute] Guid bindingId,
        [FromBody] UpdateEdgeHostPlcBindingCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(
            command with { EdgeHostId = id, BindingId = bindingId },
            cancellationToken));
    }

    [HttpPost("{id:guid}/plc-bindings/{bindingId:guid}/enable")]
    public async Task<IActionResult> EnablePlcBinding(
        [FromRoute] Guid id,
        [FromRoute] Guid bindingId,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(
            new EnableEdgeHostPlcBindingCommand(id, bindingId),
            cancellationToken));
    }

    [HttpPost("{id:guid}/plc-bindings/{bindingId:guid}/disable")]
    public async Task<IActionResult> DisablePlcBinding(
        [FromRoute] Guid id,
        [FromRoute] Guid bindingId,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(
            new DisableEdgeHostPlcBindingCommand(id, bindingId),
            cancellationToken));
    }

    [HttpDelete("{id:guid}/plc-bindings/{bindingId:guid}")]
    public async Task<IActionResult> RemovePlcBinding(
        [FromRoute] Guid id,
        [FromRoute] Guid bindingId,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(
            new RemoveEdgeHostPlcBindingCommand(id, bindingId),
            cancellationToken));
    }
}
