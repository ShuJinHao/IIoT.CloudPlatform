using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.Recipes;
using IIoT.ProductionService.Queries.Recipes;
using IIoT.SharedKernel.Paging;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

/// <summary>
/// 配方科：生产工艺与参数中枢 (负责通用与特调配方的管理下发)
/// </summary>
[Route("api/v1/[controller]")]
[ApiController]
[Tags("配方科 - 生产工艺与参数")]
public class RecipeController : ApiControllerBase
{
    /// <summary>
    /// 获取当前登录人管辖范围内的配方分页列表
    /// </summary>
    /// <remarks>
    /// 自动根据登录人的双维权限 (工序与机台) 过滤数据。列表不包含庞大的 JSONB 参数。
    /// </remarks>
    /// <param name="pagination">分页参数</param>
    /// <param name="keyword">搜索关键字</param>
    [HttpGet]
    // 🌟 核心修复：把复杂的 record 拆开，让框架先成功绑定基础的 Pagination 和 keyword
    public async Task<IActionResult> GetPagedList([FromQuery] Pagination pagination, [FromQuery] string? keyword = null)
    {
        // 🌟 防御性初始化：如果前端连分页参数都没传，保证它有一个默认值，绝对不为 null
        pagination ??= new Pagination();

        // 🌟 手动组装严格的 CQRS 契约对象，再发给 MediatR
        var query = new GetMyRecipesPagedQuery(pagination, keyword);
        var result = await Sender.Send(query);

        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 极速获取单体配方详情 (包含 JSONB 工艺参数)
    /// </summary>
    /// <remarks>
    /// 结合 Redis 缓存极速读取，内存级 ABAC 防越权窥探。
    /// </remarks>
    /// <param name="id">配方 ID</param>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetDetail([FromRoute] Guid id)
    {
        var query = new GetRecipeByIdQuery(id);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 创建全新生产配方 (初始版本 V1.0)
    /// </summary>
    /// <remarks>
    /// 传 DeviceId 即为特调配方，不传即为通用配方。底层自动进行权限拦截校验。
    /// </remarks>
    /// <param name="command">配方核心资料与 JSONB 参数</param>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRecipeCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 升级配方版本（基于旧版本创建新版本，旧版本自动归档）
    /// </summary>
    /// <param name="id">源配方 ID</param>
    /// <param name="command">新版本号和参数</param>
    [HttpPost("{id}/upgrade")]
    public async Task<IActionResult> UpgradeVersion([FromRoute] Guid id, [FromBody] UpgradeRecipeVersionCommand command)
    {
        var result = await Sender.Send(command with { SourceRecipeId = id });
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 物理删除配方
    /// </summary>
    /// <param name="id">配方 ID</param>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        var command = new DeleteRecipeCommand(id);
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 根据设备ID获取该设备可用的配方列表（含完整 JSONB 工艺参数）
    /// </summary>
    /// <remarks>
    /// 边缘端 RecipeSyncTask 定时拉取专用接口。
    /// 返回该设备的专属特调配方 + 所属工序的通用配方，不走员工权限校验。
    /// </remarks>
    /// <param name="deviceId">设备 ID</param>
    [HttpGet("device/{deviceId}")]
    public async Task<IActionResult> GetByDeviceId([FromRoute] Guid deviceId)
    {
        var query = new GetRecipesByDeviceIdQuery(deviceId);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}