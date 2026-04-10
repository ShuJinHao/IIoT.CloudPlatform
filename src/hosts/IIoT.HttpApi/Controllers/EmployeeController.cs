using IIoT.EmployeeService.Commands.Employees;
using IIoT.EmployeeService.Queries.Employees;
using IIoT.HttpApi.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

/// <summary>
/// 人事科：组织架构与管辖权中枢 (负责员工业务档案与双维 ABAC 权限分配)
/// </summary>
[Route("api/v1/[controller]")]
[ApiController]
[Tags("人事科 - 组织与管辖权管理")]
public class EmployeeController : ApiControllerBase
{
    /// <summary>
    /// 获取员工分页列表
    /// </summary>
    /// <param name="query">分页与搜索参数</param>
    [HttpGet]
    public async Task<IActionResult> GetPagedList([FromQuery] GetEmployeePagedListQuery query)
    {
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 获取指定员工的业务档案详情
    /// </summary>
    /// <param name="id">员工 ID (SharedId)</param>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetDetail([FromRoute] Guid id)
    {
        var query = new GetEmployeeDetailQuery(id);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 获取指定员工当前的双维数据管辖权 (工序与机台)
    /// </summary>
    /// <param name="id">员工 ID (SharedId)</param>
    [HttpGet("{id}/access")]
    public async Task<IActionResult> GetAccess([FromRoute] Guid id)
    {
        var query = new GetEmployeeAccessQuery(id);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 员工入职建档 (灵魂绑定)
    /// </summary>
    /// <remarks>
    /// 必须先在保安科 (Identity) 开户拿到 SharedId 后，将其作为入参传入此接口建档。
    /// </remarks>
    /// <param name="command">入职资料 (包含 SharedId, 工号, 姓名等)</param>
    [HttpPost]
    public async Task<IActionResult> Onboard([FromBody] OnboardEmployeeCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 更新员工基础业务资料 (工号、姓名、联系方式等)
    /// </summary>
    /// <param name="id">员工 ID</param>
    /// <param name="command">需要更新的资料</param>
    [HttpPut("{id}/profile")]
    public async Task<IActionResult> UpdateProfile([FromRoute] Guid id, [FromBody] UpdateEmployeeProfileCommand command)
    {
        // 保证路由 ID 与指令体中的 ID 一致 (防御性设计也可在 Command 中带入)
        var result = await Sender.Send(command with { EmployeeId = id });
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
    /// <summary>
    /// 决定该员工能操作哪些机台。
    /// </summary>
    /// <param name="id">员工 ID</param>
    /// <param name="command">包含允许访问的 ProcessId 列表和 DeviceId 列表</param>
    [HttpPut("{id}/access")]
    public async Task<IActionResult> UpdateAccess([FromRoute] Guid id, [FromBody] UpdateEmployeeAccessCommand command)
    {
        var result = await Sender.Send(command with { EmployeeId = id });
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 停用员工 (保留档案，但剥夺系统业务操作能力)
    /// </summary>
    /// <param name="id">员工 ID</param>
    [HttpPut("{id}/deactivate")]
    public async Task<IActionResult> Deactivate([FromRoute] Guid id)
    {
        var command = new DeactivateEmployeeCommand(id);
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 员工离职 / 物理销毁业务档案
    /// </summary>
    /// <remarks>
    /// 通常建议使用保安科的 DeleteAccount 触发级联删除，此处仅作为独立业务线的补充。
    /// </remarks>
    /// <param name="id">员工 ID</param>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Terminate([FromRoute] Guid id)
    {
        var command = new TerminateEmployeeCommand(id);
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}