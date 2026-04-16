using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.Recipes;
using IIoT.ProductionService.Queries.Recipes;
using IIoT.SharedKernel.Paging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

/// <summary>
/// 配方接口。
/// </summary>
[Authorize]
[Route("api/v1/[controller]")]
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Tags("配方科 - 生产工艺与参数")]
public class RecipeController : ApiControllerBase
{
    /// <summary>
    /// 获取配方分页列表。
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetPagedList([FromQuery] Pagination pagination, [FromQuery] string? keyword = null)
    {
        var result = await Sender.Send(new GetMyRecipesPagedQuery(pagination, keyword));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 获取配方详情。
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetDetail([FromRoute] Guid id)
    {
        var result = await Sender.Send(new GetRecipeByIdQuery(id));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 创建配方。
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRecipeCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 升级配方版本。
    /// </summary>
    [HttpPost("{id}/upgrade")]
    public async Task<IActionResult> UpgradeVersion([FromRoute] Guid id, [FromBody] UpgradeRecipeVersionCommand command)
    {
        var result = await Sender.Send(command with { SourceRecipeId = id });
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 删除配方。
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        var result = await Sender.Send(new DeleteRecipeCommand(id));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 获取设备可用配方列表。
    /// </summary>
    [HttpGet("device/{deviceId}")]
    public async Task<IActionResult> GetByDeviceId([FromRoute] Guid deviceId)
    {
        var result = await Sender.Send(new GetRecipesByDeviceIdQuery(deviceId));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}
