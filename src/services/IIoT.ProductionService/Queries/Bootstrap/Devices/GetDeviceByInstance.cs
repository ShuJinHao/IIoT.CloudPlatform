using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.ProductionService.Security;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Devices;

sealed record DeviceBootstrapIdentity(
    Guid Id,
    string DeviceName,
    string ClientCode,
    Guid ProcessId);

public record DeviceIdentityDto(
    Guid Id,
    string DeviceName,
    string ClientCode,
    Guid ProcessId,
    string UploadAccessToken,
    DateTimeOffset UploadAccessTokenExpiresAtUtc,
    string SessionKind = "Formal",
    string? ActivationToken = null,
    DateTimeOffset? ActivationTokenExpiresAtUtc = null,
    Guid? GenerationId = null
);

public sealed record BootstrapDeviceSessionResult(
    DeviceIdentityDto DeviceIdentity,
    string? RefreshToken,
    DateTimeOffset? RefreshTokenExpiresAtUtc);

public record GetDeviceByInstanceQuery(
    string Code,
    string? BootstrapSecret = null
) : IAnonymousBootstrapQuery<Result<BootstrapDeviceSessionResult>>;

public class GetDeviceByInstanceHandler(
    IReadRepository<Device> deviceRepository,
    IJwtTokenGenerator jwtTokenGenerator,
    IRefreshTokenService refreshTokenService,
    IEdgeInstallerGenerationStore installerGenerationStore
) : IQueryHandler<GetDeviceByInstanceQuery, Result<BootstrapDeviceSessionResult>>
{
    public async Task<Result<BootstrapDeviceSessionResult>> Handle(
        GetDeviceByInstanceQuery request,
        CancellationToken cancellationToken)
    {
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure("设备寻址失败：设备寻址码不能为空。");
        }

        var spec = new DeviceByCodeSpec(code);
        var device = await deviceRepository.GetSingleOrDefaultAsync(spec, cancellationToken);

        if (device is null)
        {
            return Result.Failure($"设备寻址失败：未找到寻址码为 [{code}] 的设备。");
        }

        var pendingCredentials = await installerGenerationStore
            .GetPendingByClientCodeAsync(
                device.Code,
                DateTime.UtcNow,
                cancellationToken);
        var pending = pendingCredentials.FirstOrDefault(candidate =>
            BootstrapSecretHasher.Verify(
                request.BootstrapSecret,
                candidate.SecretHash));
        if (pending is not null)
        {
            var activationToken = jwtTokenGenerator.GenerateEdgeActivationToken(
                pending.GenerationId,
                device.Id,
                device.Code,
                device.ProcessId);
            return Result.Success(new BootstrapDeviceSessionResult(
                new DeviceIdentityDto(
                    device.Id,
                    device.DeviceName,
                    device.Code,
                    device.ProcessId,
                    string.Empty,
                    activationToken.ExpiresAtUtc,
                    "ActivationOnly",
                    activationToken.Token,
                    activationToken.ExpiresAtUtc,
                    pending.GenerationId),
                null,
                null));
        }

        if (!BootstrapSecretHasher.Verify(request.BootstrapSecret, device.BootstrapSecretHash))
        {
            return Result.Unauthorized("设备启动认证失败：启动密钥无效。");
        }

        return await IssueSessionAsync(
            new DeviceBootstrapIdentity(
                device.Id,
                device.DeviceName,
                device.Code,
                device.ProcessId),
            cancellationToken);
    }

    private async Task<Result<BootstrapDeviceSessionResult>> IssueSessionAsync(
        DeviceBootstrapIdentity identity,
        CancellationToken cancellationToken)
    {
        var accessToken = jwtTokenGenerator.GenerateEdgeDeviceToken(
            identity.Id,
            identity.ClientCode,
            identity.ProcessId);
        var refreshToken = await refreshTokenService.IssueAsync(
            IIoTClaimTypes.EdgeDeviceActor,
            identity.Id,
            cancellationToken);

        return Result.Success(new BootstrapDeviceSessionResult(
            new DeviceIdentityDto(
                identity.Id,
                identity.DeviceName,
                identity.ClientCode,
                identity.ProcessId,
                accessToken.Token,
                accessToken.ExpiresAtUtc),
            refreshToken.Token,
            refreshToken.ExpiresAtUtc));
    }
}
