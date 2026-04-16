using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.Commands.Devices;
using IIoT.ProductionService.Queries.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

/// <summary>
/// 设备接口。
/// </summary>
[Authorize]
[Route("api/v1/[controller]")]
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Tags("设备科 - 物理终端与寻址")]
public class DeviceController : ApiControllerBase
{
    /// <summary>
    /// 按实例标识获取设备。
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
    /// 获取设备分页列表。
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetPagedList([FromQuery] GetMyDevicesPagedQuery query)
    {
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 获取全量设备列表。
    /// </summary>
    [HttpGet("all")]
    public async Task<IActionResult> GetAllActive()
    {
        var result = await Sender.Send(new GetAllDevicesQuery());
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 注册设备。
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Register([FromBody] RegisterDeviceCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 更新设备基础资料。
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateProfile([FromRoute] Guid id, [FromBody] UpdateDeviceProfileCommand command)
    {
        var result = await Sender.Send(command with { DeviceId = id });
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 删除设备。
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        var command = new DeleteDeviceCommand(id);
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}
