using IIoT.HttpApi.Infrastructure;
using IIoT.HttpApi.Infrastructure.Oidc;
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
    [EnableRateLimiting(HttpApiRateLimitPolicies.PasswordLogin)]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginUserCommand command,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken);
        if (result.IsSuccess && result.Value is not null)
        {
            RefreshTokenResponseFilter.SetHeaders(
                HttpContext,
                result.Value.RefreshToken,
                result.Value.RefreshTokenExpiresAtUtc,
                result.Value.AccessTokenExpiresAtUtc);

            var oidcSessionService = HttpContext.RequestServices.GetRequiredService<ICloudOidcSessionService>();
            await oidcSessionService.SignInAsync(
                HttpContext,
                command.EmployeeNo,
                cancellationToken);
        }

        return ReturnBodyResult(result, session => session.AccessToken);
    }

    [AllowAnonymous]
    [EnableRateLimiting(HttpApiRateLimitPolicies.Refresh)]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(
        [FromHeader(Name = RefreshTokenHeaderNames.RefreshToken)] string refreshToken,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new RefreshHumanIdentityCommand(refreshToken), cancellationToken);
        if (result.IsSuccess && result.Value is not null)
        {
            RefreshTokenResponseFilter.SetHeaders(
                HttpContext,
                result.Value.RefreshToken,
                result.Value.RefreshTokenExpiresAtUtc,
                result.Value.AccessTokenExpiresAtUtc);
        }

        return ReturnBodyResult(result, session => session.AccessToken);
    }

    [AllowAnonymous]
    [EnableRateLimiting(HttpApiRateLimitPolicies.EdgeOperatorLogin)]
    [HttpPost("edge-login")]
    public async Task<IActionResult> EdgeLogin(
        [FromBody] EdgeOperatorLoginCommand command,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken);
        if (result.IsSuccess && result.Value is not null)
        {
            RefreshTokenResponseFilter.SetHeaders(
                HttpContext,
                result.Value.RefreshToken,
                result.Value.RefreshTokenExpiresAtUtc,
                result.Value.AccessTokenExpiresAtUtc);
        }

        return ReturnBodyResult(result, session => session.AccessToken);
    }

    [EnableRateLimiting(HttpApiRateLimitPolicies.GeneralApi)]
    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(command, cancellationToken));
    }

    [EnableRateLimiting(HttpApiRateLimitPolicies.GeneralApi)]
    [HttpPut("password/reset")]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(command, cancellationToken));
    }

    [EnableRateLimiting(HttpApiRateLimitPolicies.GeneralApi)]
    [HttpGet("roles")]
    public async Task<IActionResult> GetAllRoles(CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new GetAllRolesQuery(), cancellationToken));
    }

    [EnableRateLimiting(HttpApiRateLimitPolicies.GeneralApi)]
    [HttpPost("roles")]
    public async Task<IActionResult> DefineRolePolicy(
        [FromBody] DefineRolePolicyCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(command, cancellationToken));
    }

    [EnableRateLimiting(HttpApiRateLimitPolicies.GeneralApi)]
    [HttpGet("roles/{roleName}/permissions")]
    public async Task<IActionResult> GetRolePermissions(
        [FromRoute] string roleName,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new GetRolePermissionsQuery(roleName), cancellationToken));
    }

    [EnableRateLimiting(HttpApiRateLimitPolicies.GeneralApi)]
    [HttpPut("roles/{roleName}/permissions")]
    public async Task<IActionResult> UpdateRolePermissions(
        [FromRoute] string roleName,
        [FromBody] List<string> permissions,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new UpdateRolePermissionsCommand(roleName, permissions), cancellationToken));
    }

    [EnableRateLimiting(HttpApiRateLimitPolicies.GeneralApi)]
    [HttpGet("permissions")]
    public async Task<IActionResult> GetAllPermissions(CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new GetAllDefinedPermissionsQuery(), cancellationToken));
    }

    [EnableRateLimiting(HttpApiRateLimitPolicies.GeneralApi)]
    [HttpGet("users/{userId}/permissions")]
    public async Task<IActionResult> GetUserPersonalPermissions(
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(new GetUserPersonalPermissionsQuery(userId), cancellationToken));
    }

    [EnableRateLimiting(HttpApiRateLimitPolicies.GeneralApi)]
    [HttpPut("users/{userId}/permissions")]
    public async Task<IActionResult> UpdateUserPermissions(
        [FromRoute] Guid userId,
        [FromBody] UpdateUserPermissionsCommand command,
        CancellationToken cancellationToken)
    {
        return ReturnResult(await Sender.Send(command with { UserId = userId }, cancellationToken));
    }
}
