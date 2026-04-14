using IIoT.EmployeeService.Commands.Employees;
using IIoT.EmployeeService.Queries.Employees;
using IIoT.HttpApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

/// <summary>
/// 员工档案与权限接口。
/// </summary>
[Route("api/v1/[controller]")]
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Tags("人事科 - 组织与管辖权管理")]
public class EmployeeController : ApiControllerBase
{
    /// <summary>
    /// 获取员工分页列表。
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetPagedList([FromQuery] GetEmployeePagedListQuery query)
    {
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 获取员工详情。
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetDetail([FromRoute] Guid id)
    {
        var result = await Sender.Send(new GetEmployeeDetailQuery(id));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 获取员工访问范围。
    /// </summary>
    [HttpGet("{id}/access")]
    public async Task<IActionResult> GetAccess([FromRoute] Guid id)
    {
        var result = await Sender.Send(new GetEmployeeAccessQuery(id));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 员工入职建档。
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Onboard([FromBody] OnboardEmployeeCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 更新员工基础资料。
    /// </summary>
    [HttpPut("{id}/profile")]
    public async Task<IActionResult> UpdateProfile([FromRoute] Guid id, [FromBody] UpdateEmployeeProfileCommand command)
    {
        var result = await Sender.Send(command with { EmployeeId = id });
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 更新员工访问范围。
    /// </summary>
    [HttpPut("{id}/access")]
    public async Task<IActionResult> UpdateAccess([FromRoute] Guid id, [FromBody] UpdateEmployeeAccessCommand command)
    {
        var result = await Sender.Send(command with { EmployeeId = id });
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 停用员工。
    /// </summary>
    [HttpPut("{id}/deactivate")]
    public async Task<IActionResult> Deactivate([FromRoute] Guid id)
    {
        var result = await Sender.Send(new DeactivateEmployeeCommand(id));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 删除员工档案。
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Terminate([FromRoute] Guid id)
    {
        var result = await Sender.Send(new TerminateEmployeeCommand(id));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}
