using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.Recipes;
using IIoT.ProductionService.Queries.Recipes;
using IIoT.SharedKernel.Paging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

[Authorize]
[Route("api/v1/human/recipes")]
[ApiController]
[Tags("Human Recipes")]
public class HumanRecipeController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetPagedList([FromQuery] Pagination pagination, [FromQuery] string? keyword = null)
    {
        return ReturnResult(await Sender.Send(new GetMyRecipesPagedQuery(pagination, keyword)));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDetail([FromRoute] Guid id)
    {
        return ReturnResult(await Sender.Send(new GetRecipeByIdQuery(id)));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRecipeCommand command)
    {
        return ReturnResult(
            await Sender.Send(command),
            recipeId => $"/api/v1/human/recipes/{recipeId}");
    }

    [HttpPost("{id}/upgrade")]
    public async Task<IActionResult> UpgradeVersion([FromRoute] Guid id, [FromBody] UpgradeRecipeVersionCommand command)
    {
        return ReturnResult(
            await Sender.Send(command with { SourceRecipeId = id }),
            recipeId => $"/api/v1/human/recipes/{recipeId}");
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        return ReturnResult(await Sender.Send(new DeleteRecipeCommand(id)));
    }
}
