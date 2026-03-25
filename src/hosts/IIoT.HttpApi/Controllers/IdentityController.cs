using IIoT.HttpApi.Infrastructure;
using IIoT.IdentityService.Commands;
using IIoT.IdentityService.Queries;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

/// <summary>
/// 保安科：身份与安全认证中枢 (负责登录、发牌、底层账号与权限点分配)
/// </summary>
[Route("api/v1/[controller]")]
[ApiController]
[Tags("保安科 - 身份与安全认证")]
public class IdentityController : ApiControllerBase
{
    /// <summary>
    /// 极速登录并获取 JWT 令牌
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginUserCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 修改当前登录账号的密码
    /// </summary>
    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 管理员强制重置指定员工的密码 (不需要旧密码)
    /// </summary>
    [HttpPut("password/reset")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 获取全系统底层角色策略列表
    /// </summary>
    [HttpGet("roles")]
    public async Task<IActionResult> GetAllRoles()
    {
        var result = await Sender.Send(new GetAllRolesQuery());
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 定义全新的系统角色
    /// </summary>
    [HttpPost("roles")]
    public async Task<IActionResult> DefineRolePolicy([FromBody] DefineRolePolicyCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 获取指定角色当前绑定的全部权限点
    /// </summary>
    [HttpGet("roles/{roleName}/permissions")]
    public async Task<IActionResult> GetRolePermissions([FromRoute] string roleName)
    {
        var query = new GetRolePermissionsQuery(roleName);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 更新指定角色的行为权限点 (Claims 组)
    /// </summary>
    [HttpPut("roles/{roleName}/permissions")]
    public async Task<IActionResult> UpdateRolePermissions(
        [FromRoute] string roleName,
        [FromBody] List<string> permissions)
    {
        var command = new UpdateRolePermissionsCommand(roleName, permissions);
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 获取系统全部已定义的权限点 (动态聚合，按模块分组)
    /// </summary>
    [HttpGet("permissions/all")]
    public async Task<IActionResult> GetAllPermissions()
    {
        var result = await Sender.Send(new GetAllDefinedPermissionsQuery());
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 获取指定员工的个人特批权限点列表 (不含角色继承的权限)
    /// </summary>
    [HttpGet("users/{userId}/permissions")]
    public async Task<IActionResult> GetUserPersonalPermissions([FromRoute] Guid userId)
    {
        var query = new GetUserPersonalPermissionsQuery(userId);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 管理员为指定员工设置个人特批权限点 (全量同步，与角色权限是并集关系)
    /// </summary>
    [HttpPut("users/{userId}/permissions")]
    public async Task<IActionResult> UpdateUserPermissions(
        [FromRoute] Guid userId,
        [FromBody] UpdateUserPermissionsCommand command)
    {
        var result = await Sender.Send(command with { UserId = userId });
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// WPF 边缘端专用登录（含设备绑定校验）
    /// </summary>
    [HttpPost("device-login")]
    public async Task<IActionResult> DeviceLogin([FromBody] DeviceLoginCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}
