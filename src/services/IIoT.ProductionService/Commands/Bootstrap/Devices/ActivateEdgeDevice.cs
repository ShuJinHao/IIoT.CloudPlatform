using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.ProductionService.Queries.Devices;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Bootstrap.Devices;

public sealed record ActivateEdgeDeviceCommand(
    Guid GenerationId,
    string ClientCode,
    int Pid,
    string ModuleId,
    string PluginVersion,
    string PackageSha256,
    DateTime ReadyAtUtc)
    : ICommand<Result<BootstrapDeviceSessionResult>>;

public sealed record ConfirmEdgeDeviceActivationCommand(
    Guid GenerationId,
    int Pid,
    DateTime ReadyAtUtc)
    : ICommand<Result>;

public sealed class ActivateEdgeDeviceHandler(
    ICurrentUser currentUser,
    IReadRepository<Device> deviceRepository,
    IEdgeInstallerGenerationStore generationStore,
    IJwtTokenGenerator jwtTokenGenerator,
    IRefreshTokenService refreshTokenService)
    : ICommandHandler<
        ActivateEdgeDeviceCommand,
        Result<BootstrapDeviceSessionResult>>
{
    public async Task<Result<BootstrapDeviceSessionResult>> Handle(
        ActivateEdgeDeviceCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                currentUser.ActorType,
                IIoTClaimTypes.EdgeActivationActor,
                StringComparison.Ordinal)
            || currentUser.DeviceId is not { } tokenDeviceId
            || currentUser.InstallerGenerationId != request.GenerationId)
        {
            return Result.Unauthorized("激活会话与安装记录不匹配。");
        }

        if (request.GenerationId == Guid.Empty
            || request.Pid <= 0
            || string.IsNullOrWhiteSpace(request.ClientCode)
            || string.IsNullOrWhiteSpace(request.ModuleId)
            || string.IsNullOrWhiteSpace(request.PluginVersion)
            || !IsSha256(request.PackageSha256))
        {
            return Result.Invalid("设备 ready 激活证据不完整。");
        }

        var readyAtUtc = request.ReadyAtUtc.Kind == DateTimeKind.Utc
            ? request.ReadyAtUtc
            : request.ReadyAtUtc.ToUniversalTime();
        if (readyAtUtc < DateTime.UtcNow.AddHours(-1)
            || readyAtUtc > DateTime.UtcNow.AddMinutes(5))
        {
            return Result.Invalid("设备 ready 时间超出可接受范围。");
        }

        var device = await deviceRepository.GetSingleOrDefaultAsync(
            new DeviceByIdSpec(tokenDeviceId),
            cancellationToken);
        var pending = await generationStore.GetPendingAsync(
            request.GenerationId,
            tokenDeviceId,
            cancellationToken);
        if (device is null || pending is null)
        {
            return Result.Unauthorized("待激活凭证不存在或已失效。");
        }

        var clientCode = request.ClientCode.Trim().ToUpperInvariant();
        var packageSha256 = request.PackageSha256.Trim().ToLowerInvariant();
        if (!string.Equals(device.Code, clientCode, StringComparison.Ordinal)
            || !string.Equals(pending.ClientCode, clientCode, StringComparison.Ordinal)
            || !string.Equals(
                pending.ModuleId,
                request.ModuleId.Trim(),
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                pending.PluginVersion,
                request.PluginVersion.Trim(),
                StringComparison.Ordinal)
            || !string.Equals(
                pending.PackageSha256,
                packageSha256,
                StringComparison.Ordinal))
        {
            return Result.Forbidden("设备 ready 证据与签名安装计划不一致。");
        }

        var activationAttempt = await generationStore.TryActivateAsync(
                request.GenerationId,
                device.Id,
                request.Pid,
                readyAtUtc,
                cancellationToken);
        if (activationAttempt is EdgeInstallerActivationAttempt.Expired
            or EdgeInstallerActivationAttempt.Unavailable
            or EdgeInstallerActivationAttempt.AlreadyConfirmed)
        {
            return Result.Unauthorized("待激活凭证不存在或已失效。");
        }
        if (activationAttempt == EdgeInstallerActivationAttempt.Conflict)
        {
            return Result.Failure("设备已使用不同 ready 证据激活，拒绝重放。");
        }
        // Pending -> Activating is durable before formal credentials are
        // issued. Replacement issuance and revocation of prior refresh
        // sessions are one database transaction: a failed issuance therefore
        // cannot strand the old client without a usable session.
        var refreshToken = await refreshTokenService.IssueReplacingAsync(
            IIoTClaimTypes.EdgeDeviceActor,
            device.Id,
            "installer-v3-device-activated",
            cancellationToken);
        var accessToken = jwtTokenGenerator.GenerateEdgeDeviceToken(
            device.Id,
            device.Code,
            device.ProcessId);

        return Result.Success(new BootstrapDeviceSessionResult(
            new DeviceIdentityDto(
                device.Id,
                device.DeviceName,
                device.Code,
                device.ProcessId,
                accessToken.Token,
                accessToken.ExpiresAtUtc,
                "Active",
                null,
                null,
                request.GenerationId),
            refreshToken.Token,
            refreshToken.ExpiresAtUtc));
    }

    private static bool IsSha256(string? value)
        => value?.Trim() is { Length: 64 } normalized
           && normalized.All(Uri.IsHexDigit);
}

public sealed class ConfirmEdgeDeviceActivationHandler(
    ICurrentUser currentUser,
    IEdgeInstallerGenerationStore generationStore)
    : ICommandHandler<ConfirmEdgeDeviceActivationCommand, Result>
{
    public async Task<Result> Handle(
        ConfirmEdgeDeviceActivationCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                currentUser.ActorType,
                IIoTClaimTypes.EdgeDeviceActor,
                StringComparison.Ordinal)
            || currentUser.DeviceId is not { } deviceId
            || string.IsNullOrWhiteSpace(currentUser.ClientCode))
        {
            return Result.Unauthorized("只有已签发的设备会话可确认激活。");
        }

        if (request.GenerationId == Guid.Empty || request.Pid <= 0)
            return Result.Invalid("激活确认证据不完整。");

        var readyAtUtc = request.ReadyAtUtc.Kind == DateTimeKind.Utc
            ? request.ReadyAtUtc
            : request.ReadyAtUtc.ToUniversalTime();
        var pending = await generationStore.GetPendingAsync(
            request.GenerationId,
            deviceId,
            cancellationToken);
        if (pending is null
            || !string.Equals(
                pending.ClientCode,
                currentUser.ClientCode,
                StringComparison.Ordinal))
        {
            return Result.Forbidden("激活确认与当前设备不匹配。");
        }

        var attempt = await generationStore.ConfirmActivationAsync(
            request.GenerationId,
            deviceId,
            request.Pid,
            readyAtUtc,
            cancellationToken);
        return attempt switch
        {
            EdgeInstallerActivationAttempt.Confirmed
                or EdgeInstallerActivationAttempt.AlreadyConfirmed
                => Result.Success(),
            EdgeInstallerActivationAttempt.Conflict
                => Result.Failure("激活确认与 ready 证据冲突。"),
            _ => Result.Failure("设备尚未完成可确认的 ready 激活。")
        };
    }
}
