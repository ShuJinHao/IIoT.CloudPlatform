using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.Devices;
using IIoT.ProductionService.Queries.Devices;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

/// <summary>
/// 设备科:物理机台与终端中枢 (负责机台寻址、注册与管辖列表维护)
/// </summary>
[Route("api/v1/[controller]")]
[ApiController]
[Tags("设备科 - 物理终端与寻址")]
public class DeviceController : ApiControllerBase
{
    /// <summary>
    /// 上位机轮询认证(凭 MAC + ClientCode 换取 DeviceId)
    /// </summary>
    [HttpGet("instance")]
    public async Task<IActionResult> GetByInstance(
        [FromQuery] string macAddress,
        [FromQuery] string clientCode)
    {
        var query = new GetDeviceByInstanceQuery(macAddress, clientCode);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 获取当前登录人管辖范围内的设备分页列表
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetPagedList([FromQuery] GetMyDevicesPagedQuery query)
    {
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 获取全量活跃设备列表 (供管辖权分配下拉选择器使用,不做 ABAC 数据级过滤)
    /// </summary>
    [HttpGet("all")]
    public async Task<IActionResult> GetAllActive()
    {
        var result = await Sender.Send(new GetAllActiveDevicesQuery());
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 注册全新物理设备终端
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Register([FromBody] RegisterDeviceCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 更新设备的业务基础档案 (设备名称)
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateProfile([FromRoute] Guid id, [FromBody] UpdateDeviceProfileCommand command)
    {
        var result = await Sender.Send(command with { DeviceId = id });
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 停用设备 (软删除控制)
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Deactivate([FromRoute] Guid id)
    {
        var command = new DeactivateDeviceCommand(id);
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}