using System.Security.Claims;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace IIoT.HttpApi.Infrastructure.Oidc;

public sealed class CloudOidcSessionService(
    ICloudOidcUserProfileService profileService,
    IAuditTrailService auditTrailService,
    IOptions<OidcProviderOptions> options) : ICloudOidcSessionService
{
    public async Task SignInAsync(
        HttpContext httpContext,
        string employeeNo,
        CancellationToken cancellationToken = default)
    {
        var profile = await profileService.GetByEmployeeNoAsync(employeeNo, cancellationToken);
        if (profile is null || !profile.AccountEnabled || !profile.EmployeeActive)
        {
            await auditTrailService.TryWriteAsync(
                new AuditTrailEntry(
                    profile?.UserId,
                    employeeNo,
                    CloudOidcDefaults.LoginAuditOperation,
                    "CloudOidcSession",
                    employeeNo,
                    DateTime.UtcNow,
                    false,
                    "OIDC 登录会话未写入。",
                    profile is null ? "Cloud 用户资料不存在。" : "Cloud 账号或员工状态不可用。"),
                cancellationToken);
            return;
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, profile.UserId.ToString()),
                new Claim(ClaimTypes.Name, profile.EmployeeNo),
                new Claim("employee_no", profile.EmployeeNo),
                new Claim("display_name", profile.RealName)
            ],
            CloudOidcDefaults.SessionScheme);

        var properties = new AuthenticationProperties
        {
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(options.Value.SessionIdleMinutes),
            IsPersistent = false
        };

        await httpContext.SignInAsync(
            CloudOidcDefaults.SessionScheme,
            new ClaimsPrincipal(identity),
            properties);

        await auditTrailService.TryWriteAsync(
            new AuditTrailEntry(
                profile.UserId,
                profile.EmployeeNo,
                CloudOidcDefaults.LoginAuditOperation,
                "CloudOidcSession",
                profile.UserId.ToString(),
                DateTime.UtcNow,
                true,
                "OIDC 登录会话已写入。"),
            cancellationToken);
    }

    public Task SignOutAsync(HttpContext httpContext)
    {
        return httpContext.SignOutAsync(CloudOidcDefaults.SessionScheme);
    }
}
