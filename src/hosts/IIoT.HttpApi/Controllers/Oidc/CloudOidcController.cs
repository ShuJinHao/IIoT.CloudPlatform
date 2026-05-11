using System.Security.Claims;
using IIoT.HttpApi.Infrastructure.Oidc;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Identity;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IIoT.HttpApi.Controllers.Oidc;

[ApiExplorerSettings(IgnoreApi = true)]
public sealed class CloudOidcController(
    ICloudOidcUserProfileService profileService,
    ICloudOidcSessionService sessionService,
    IAuditTrailService auditTrailService,
    IOptions<OidcProviderOptions> options) : Controller
{
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpGet("~/connect/authorize")]
    public async Task<IActionResult> Authorize(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OIDC authorize request is unavailable.");

        if (string.IsNullOrWhiteSpace(request.State) || string.IsNullOrWhiteSpace(request.Nonce))
        {
            await WriteAuditAsync(
                CloudOidcDefaults.AuthorizeAuditOperation,
                null,
                null,
                request.ClientId,
                false,
                "OIDC authorize rejected.",
                "state or nonce is missing.",
                cancellationToken);

            return ForbidWithOpenIddictError(
                Errors.InvalidRequest,
                "state and nonce are required.");
        }

        var session = await HttpContext.AuthenticateAsync(CloudOidcDefaults.SessionScheme);
        if (!TryGetSessionUserId(session.Principal, out var userId))
        {
            await WriteAuditAsync(
                CloudOidcDefaults.AuthorizeAuditOperation,
                null,
                null,
                request.ClientId,
                false,
                "OIDC authorize 被拒绝。",
                "Cloud OIDC 会话不存在或无效。",
                cancellationToken);

            return Challenge(CloudOidcDefaults.SessionScheme);
        }

        var profile = await profileService.GetByUserIdAsync(userId, cancellationToken);
        if (profile is null || !profile.AccountEnabled || !profile.EmployeeActive)
        {
            await WriteAuditAsync(
                CloudOidcDefaults.AuthorizeAuditOperation,
                profile?.UserId ?? userId,
                profile?.EmployeeNo,
                request.ClientId,
                false,
                "OIDC authorize 被拒绝。",
                profile is null ? "Cloud 用户资料不存在。" : "Cloud 账号或员工状态不可用。",
                cancellationToken);

            return ForbidWithOpenIddictError(
                Errors.AccessDenied,
                "Cloud 账号或员工状态不可用。");
        }

        var principal = CreatePrincipal(profile, request.GetScopes());

        await WriteAuditAsync(
            CloudOidcDefaults.AuthorizeAuditOperation,
            profile.UserId,
            profile.EmployeeNo,
            request.ClientId,
            true,
            "OIDC authorize 成功。",
            null,
            cancellationToken);

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpPost("~/connect/token")]
    public async Task<IActionResult> Exchange(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OIDC token request is unavailable.");

        if (!request.IsAuthorizationCodeGrantType())
        {
            await WriteAuditAsync(
                CloudOidcDefaults.TokenAuditOperation,
                null,
                null,
                request.ClientId,
                false,
                "OIDC token exchange 被拒绝。",
                "不支持的授权类型。",
                cancellationToken);

            return ForbidWithOpenIddictError(
                Errors.UnsupportedGrantType,
                "Only authorization_code is supported.");
        }

        var authentication = await HttpContext.AuthenticateAsync(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        if (!authentication.Succeeded || !TryGetOpenIddictSubject(authentication.Principal, out var userId))
        {
            await WriteAuditAsync(
                CloudOidcDefaults.TokenAuditOperation,
                null,
                null,
                request.ClientId,
                false,
                "OIDC token exchange 被拒绝。",
                "authorization code 无效或已过期。",
                cancellationToken);

            return ForbidWithOpenIddictError(
                Errors.InvalidGrant,
                "The authorization code is invalid or expired.");
        }

        var profile = await profileService.GetByUserIdAsync(userId, cancellationToken);
        if (profile is null || !profile.AccountEnabled || !profile.EmployeeActive)
        {
            await WriteAuditAsync(
                CloudOidcDefaults.TokenAuditOperation,
                profile?.UserId ?? userId,
                profile?.EmployeeNo,
                request.ClientId,
                false,
                "OIDC token exchange 被拒绝。",
                profile is null ? "Cloud 用户资料不存在。" : "Cloud 账号或员工状态不可用。",
                cancellationToken);

            return ForbidWithOpenIddictError(
                Errors.InvalidGrant,
                "Cloud 账号或员工状态不可用。");
        }

        var principal = CreatePrincipal(profile, authentication.Principal?.GetScopes() ?? []);

        await WriteAuditAsync(
            CloudOidcDefaults.TokenAuditOperation,
            profile.UserId,
            profile.EmployeeNo,
            request.ClientId,
            true,
            "OIDC token exchange 成功。",
            null,
            cancellationToken);

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [Authorize(AuthenticationSchemes = OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)]
    [IgnoreAntiforgeryToken]
    [HttpGet("~/connect/userinfo")]
    [HttpPost("~/connect/userinfo")]
    public async Task<IActionResult> UserInfo(CancellationToken cancellationToken)
    {
        if (!TryGetOpenIddictSubject(User, out var userId))
        {
            await WriteAuditAsync(
                CloudOidcDefaults.UserInfoAuditOperation,
                null,
                null,
                options.Value.AicopilotClientId,
                false,
                "OIDC userinfo 被拒绝。",
                "access token 缺少 subject。",
                cancellationToken);

            return Unauthorized();
        }

        var profile = await profileService.GetByUserIdAsync(userId, cancellationToken);
        if (profile is null || !profile.AccountEnabled || !profile.EmployeeActive)
        {
            await WriteAuditAsync(
                CloudOidcDefaults.UserInfoAuditOperation,
                profile?.UserId ?? userId,
                profile?.EmployeeNo,
                options.Value.AicopilotClientId,
                false,
                "OIDC userinfo 被拒绝。",
                profile is null ? "Cloud 用户资料不存在。" : "Cloud 账号或员工状态不可用。",
                cancellationToken);

            return Unauthorized();
        }

        await WriteAuditAsync(
            CloudOidcDefaults.UserInfoAuditOperation,
            profile.UserId,
            profile.EmployeeNo,
            options.Value.AicopilotClientId,
            true,
            "OIDC userinfo 成功。",
            null,
            cancellationToken);

        return Ok(CreateUserInfoPayload(profile));
    }

    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpGet("~/connect/logout")]
    [HttpPost("~/connect/logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var session = await HttpContext.AuthenticateAsync(CloudOidcDefaults.SessionScheme);
        TryGetSessionUserId(session.Principal, out var userId);

        await sessionService.SignOutAsync(HttpContext);

        await WriteAuditAsync(
            CloudOidcDefaults.LogoutAuditOperation,
            userId == Guid.Empty ? null : userId,
            session.Principal?.FindFirstValue("employee_no"),
            options.Value.AicopilotClientId,
            true,
            "OIDC logout 已清理 Cloud OIDC 会话。",
            null,
            cancellationToken);

        return SignOut(
            new AuthenticationProperties(),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private ClaimsPrincipal CreatePrincipal(
        CloudOidcUserProfile profile,
        IEnumerable<string> requestedScopes)
    {
        var identity = new ClaimsIdentity(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            Claims.Name,
            Claims.Role);

        identity.SetClaim(Claims.Subject, profile.UserId.ToString());
        identity.SetClaim(Claims.Name, profile.RealName);
        identity.SetClaim(Claims.PreferredUsername, profile.EmployeeNo);
        identity.SetClaim("employee_no", profile.EmployeeNo);
        identity.SetClaim("employee_id", profile.UserId.ToString());
        identity.SetClaim("account_enabled", profile.AccountEnabled.ToString().ToLowerInvariant());
        identity.SetClaim("employee_active", profile.EmployeeActive.ToString().ToLowerInvariant());

        if (!string.IsNullOrWhiteSpace(profile.TenantId))
        {
            identity.SetClaim("tenant_id", profile.TenantId);
        }

        if (!string.IsNullOrWhiteSpace(profile.StatusVersion))
        {
            identity.SetClaim("status_version", profile.StatusVersion);
        }

        var principal = new ClaimsPrincipal(identity);
        var scopes = requestedScopes
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .ToHashSet(StringComparer.Ordinal);

        if (scopes.Count == 0)
        {
            scopes.Add(Scopes.OpenId);
            scopes.Add(Scopes.Profile);
        }

        principal.SetScopes(scopes);
        principal.SetResources(options.Value.AicopilotClientId);

        foreach (var claim in principal.Claims)
        {
            claim.SetDestinations(GetDestinations(claim));
        }

        return principal;
    }

    private static Dictionary<string, object?> CreateUserInfoPayload(CloudOidcUserProfile profile)
    {
        var payload = new Dictionary<string, object?>
        {
            [Claims.Subject] = profile.UserId.ToString(),
            [Claims.PreferredUsername] = profile.EmployeeNo,
            [Claims.Name] = profile.RealName,
            ["employee_no"] = profile.EmployeeNo,
            ["employee_id"] = profile.UserId.ToString(),
            ["account_enabled"] = profile.AccountEnabled,
            ["employee_active"] = profile.EmployeeActive
        };

        if (!string.IsNullOrWhiteSpace(profile.TenantId))
        {
            payload["tenant_id"] = profile.TenantId;
        }

        if (!string.IsNullOrWhiteSpace(profile.StatusVersion))
        {
            payload["status_version"] = profile.StatusVersion;
        }

        return payload;
    }

    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        return claim.Type switch
        {
            Claims.Subject => [Destinations.AccessToken, Destinations.IdentityToken],
            Claims.Name => [Destinations.AccessToken, Destinations.IdentityToken],
            Claims.PreferredUsername => [Destinations.AccessToken, Destinations.IdentityToken],
            "employee_no" => [Destinations.AccessToken, Destinations.IdentityToken],
            "employee_id" => [Destinations.AccessToken, Destinations.IdentityToken],
            "account_enabled" => [Destinations.AccessToken, Destinations.IdentityToken],
            "employee_active" => [Destinations.AccessToken, Destinations.IdentityToken],
            "tenant_id" => [Destinations.AccessToken, Destinations.IdentityToken],
            "status_version" => [Destinations.AccessToken, Destinations.IdentityToken],
            _ => [Destinations.AccessToken]
        };
    }

    private static bool TryGetSessionUserId(ClaimsPrincipal? principal, out Guid userId)
    {
        userId = Guid.Empty;
        var value = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }

    private static bool TryGetOpenIddictSubject(ClaimsPrincipal? principal, out Guid userId)
    {
        userId = Guid.Empty;
        var value = principal?.GetClaim(Claims.Subject);
        return Guid.TryParse(value, out userId);
    }

    private IActionResult ForbidWithOpenIddictError(string error, string description)
    {
        var properties = new AuthenticationProperties(
            new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
            });

        return Forbid(properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private Task WriteAuditAsync(
        string operationType,
        Guid? actorUserId,
        string? actorEmployeeNo,
        string? clientId,
        bool succeeded,
        string summary,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        return auditTrailService.TryWriteAsync(
            new AuditTrailEntry(
                actorUserId,
                actorEmployeeNo,
                operationType,
                "CloudOidc",
                string.IsNullOrWhiteSpace(clientId) ? options.Value.AicopilotClientId : clientId,
                DateTime.UtcNow,
                succeeded,
                summary,
                failureReason),
            cancellationToken);
    }
}
