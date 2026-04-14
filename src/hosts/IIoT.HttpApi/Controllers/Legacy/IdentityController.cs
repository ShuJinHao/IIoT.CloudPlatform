using IIoT.HttpApi.Infrastructure;
using IIoT.IdentityService.Commands;
using IIoT.IdentityService.Queries;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Controllers;

/// <summary>
/// 身份与权限接口。
/// </summary>
[Route("api/v1/[controller]")]
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Tags("保安科 - 身份与安全认证")]
public class IdentityController : ApiControllerBase
{
    /// <summary>
    /// 登录并获取 JWT。
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginUserCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 修改当前账号密码。
    /// </summary>
    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 重置指定员工密码。
    /// </summary>
    [HttpPut("password/reset")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 获取角色列表。
    /// </summary>
    [HttpGet("roles")]
    public async Task<IActionResult> GetAllRoles()
    {
        var result = await Sender.Send(new GetAllRolesQuery());
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 创建角色。
    /// </summary>
    [HttpPost("roles")]
    public async Task<IActionResult> DefineRolePolicy([FromBody] DefineRolePolicyCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 获取角色权限点。
    /// </summary>
    [HttpGet("roles/{roleName}/permissions")]
    public async Task<IActionResult> GetRolePermissions([FromRoute] string roleName)
    {
        var query = new GetRolePermissionsQuery(roleName);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 更新角色权限点。
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
    /// 获取系统已定义的权限点。
    /// </summary>
    [HttpGet("permissions/all")]
    public async Task<IActionResult> GetAllPermissions()
    {
        var result = await Sender.Send(new GetAllDefinedPermissionsQuery());
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 获取员工个人权限点。
    /// </summary>
    [HttpGet("users/{userId}/permissions")]
    public async Task<IActionResult> GetUserPersonalPermissions([FromRoute] Guid userId)
    {
        var query = new GetUserPersonalPermissionsQuery(userId);
        var result = await Sender.Send(query);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    /// <summary>
    /// 更新员工个人权限点。
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
    /// 边缘端登录。
    /// </summary>
    [HttpPost("device-login")]
    public async Task<IActionResult> DeviceLogin([FromBody] EdgeOperatorLoginCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}
