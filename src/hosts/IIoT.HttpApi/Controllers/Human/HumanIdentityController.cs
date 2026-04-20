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
        if (result.IsSuccess && result.Value is not null)
        {
            RefreshTokenHeaderNames.ApplyTo(
                Response,
                result.Value.RefreshToken,
                result.Value.RefreshTokenExpiresAtUtc,
                result.Value.AccessTokenExpiresAtUtc);
        }

        return ReturnBodyResult(result, session => session.AccessToken);
    }

    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(
        [FromHeader(Name = RefreshTokenHeaderNames.RefreshToken)] string refreshToken)
    {
        var result = await Sender.Send(new RefreshHumanIdentityCommand(refreshToken));
        if (result.IsSuccess && result.Value is not null)
        {
            RefreshTokenHeaderNames.ApplyTo(
                Response,
                result.Value.RefreshToken,
                result.Value.RefreshTokenExpiresAtUtc,
                result.Value.AccessTokenExpiresAtUtc);
        }

        return ReturnBodyResult(result, session => session.AccessToken);
    }

    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("edge-login")]
    public async Task<IActionResult> EdgeLogin([FromBody] EdgeOperatorLoginCommand command)
    {
        var result = await Sender.Send(command);
        if (result.IsSuccess && result.Value is not null)
        {
            RefreshTokenHeaderNames.ApplyTo(
                Response,
                result.Value.RefreshToken,
                result.Value.RefreshTokenExpiresAtUtc,
                result.Value.AccessTokenExpiresAtUtc);
        }

        return ReturnBodyResult(result, session => session.AccessToken);
    }

    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpPut("password/reset")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("roles")]
    public async Task<IActionResult> GetAllRoles()
    {
        return ReturnResult(await Sender.Send(new GetAllRolesQuery()));
    }

    [HttpPost("roles")]
    public async Task<IActionResult> DefineRolePolicy([FromBody] DefineRolePolicyCommand command)
    {
        return ReturnResult(await Sender.Send(command));
    }

    [HttpGet("roles/{roleName}/permissions")]
    public async Task<IActionResult> GetRolePermissions([FromRoute] string roleName)
    {
        return ReturnResult(await Sender.Send(new GetRolePermissionsQuery(roleName)));
    }

    [HttpPut("roles/{roleName}/permissions")]
    public async Task<IActionResult> UpdateRolePermissions(
        [FromRoute] string roleName,
        [FromBody] List<string> permissions)
    {
        return ReturnResult(await Sender.Send(new UpdateRolePermissionsCommand(roleName, permissions)));
    }

    [HttpGet("permissions")]
    public async Task<IActionResult> GetAllPermissions()
    {
        return ReturnResult(await Sender.Send(new GetAllDefinedPermissionsQuery()));
    }

    [HttpGet("users/{userId}/permissions")]
    public async Task<IActionResult> GetUserPersonalPermissions([FromRoute] Guid userId)
    {
        return ReturnResult(await Sender.Send(new GetUserPersonalPermissionsQuery(userId)));
    }

    [HttpPut("users/{userId}/permissions")]
    public async Task<IActionResult> UpdateUserPermissions(
        [FromRoute] Guid userId,
        [FromBody] UpdateUserPermissionsCommand command)
    {
        return ReturnResult(await Sender.Send(command with { UserId = userId }));
    }
}
