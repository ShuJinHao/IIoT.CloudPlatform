using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.Recipes;
using IIoT.ProductionService.Queries.Recipes;
using IIoT.SharedKernel.Paging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[Authorize]
[EnableRateLimiting(HttpApiRateLimitPolicies.GeneralApi)]
[Route("api/v1/human/recipes")]
[ApiController]
[Tags("Human Recipes")]
public class HumanRecipeController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPagedList(
        [FromQuery] Pagination pagination,
        [FromQuery] string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        return ReturnResult(await Sender.Send(new GetMyRecipesPagedQuery(pagination, keyword), cancellationToken));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDetail(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new GetRecipeByIdQuery(id), cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateRecipeCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(
            await Sender.Send(command, cancellationToken),
            recipeId => $"/api/v1/human/recipes/{recipeId}");
    }

    [HttpPost("{id}/upgrade")]
    public async Task<IActionResult> UpgradeVersion(
        [FromRoute] Guid id,
        [FromBody] UpgradeRecipeVersionCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(
            await Sender.Send(command with { SourceRecipeId = id }, cancellationToken),
            recipeId => $"/api/v1/human/recipes/{recipeId}");
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new DeleteRecipeCommand(id), cancellationToken));
    }
}
