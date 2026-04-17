using IIoT.HttpApi.Infrastructure;
using IIoT.IdentityService.Commands;
using IIoT.IdentityService.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IIoT.HttpApi.Controllers;

[Authorize]
[Route("api/v1/human/identity")]
[ApiController]
[Tags("Human Identity")]
public class HumanIdentityController : ApiControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginUserCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("edge-login")]
    public async Task<IActionResult> EdgeLogin([FromBody] EdgeOperatorLoginCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpPut("password/reset")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpGet("roles")]
    public async Task<IActionResult> GetAllRoles()
    {
        var result = await Sender.Send(new GetAllRolesQuery());
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpPost("roles")]
    public async Task<IActionResult> DefineRolePolicy([FromBody] DefineRolePolicyCommand command)
    {
        var result = await Sender.Send(command);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpGet("roles/{roleName}/permissions")]
    public async Task<IActionResult> GetRolePermissions([FromRoute] string roleName)
    {
        var result = await Sender.Send(new GetRolePermissionsQuery(roleName));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpPut("roles/{roleName}/permissions")]
    public async Task<IActionResult> UpdateRolePermissions(
        [FromRoute] string roleName,
        [FromBody] List<string> permissions)
    {
        var result = await Sender.Send(new UpdateRolePermissionsCommand(roleName, permissions));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpGet("permissions")]
    public async Task<IActionResult> GetAllPermissions()
    {
        var result = await Sender.Send(new GetAllDefinedPermissionsQuery());
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpGet("users/{userId}/permissions")]
    public async Task<IActionResult> GetUserPersonalPermissions([FromRoute] Guid userId)
    {
        var result = await Sender.Send(new GetUserPersonalPermissionsQuery(userId));
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }

    [HttpPut("users/{userId}/permissions")]
    public async Task<IActionResult> UpdateUserPermissions(
        [FromRoute] Guid userId,
        [FromBody] UpdateUserPermissionsCommand command)
    {
        var result = await Sender.Send(command with { UserId = userId });
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}
