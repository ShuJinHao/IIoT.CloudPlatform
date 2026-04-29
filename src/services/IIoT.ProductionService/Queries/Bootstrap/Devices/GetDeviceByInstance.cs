using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.ProductionService.Security;
using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.Queries.Devices;

sealed record CachedDeviceBootstrapIdentity(
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
    DateTimeOffset UploadAccessTokenExpiresAtUtc
);

public sealed record BootstrapDeviceSessionResult(
    DeviceIdentityDto DeviceIdentity,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc);

public record GetDeviceByInstanceQuery(
    string Code,
    string? BootstrapSecret = null
) : IAnonymousBootstrapQuery<Result<BootstrapDeviceSessionResult>>;

public class GetDeviceByInstanceHandler(
    IReadRepository<Device> deviceRepository,
    ICacheService cacheService,
    IJwtTokenGenerator jwtTokenGenerator,
    IRefreshTokenService refreshTokenService,
    IOptions<BootstrapAuthOptions> bootstrapAuthOptions
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

        if (bootstrapAuthOptions.Value.RequireSecret)
        {
            return await HandleWithSecretAsync(
                code,
                request.BootstrapSecret,
                cancellationToken);
        }

        var cacheKey = CacheKeys.DeviceCode(code);
        var identity = await cacheService.GetAsync<CachedDeviceBootstrapIdentity>(cacheKey, cancellationToken);
        if (identity is null)
        {
            var spec = new DeviceByCodeSpec(code);
            var device = await deviceRepository.GetSingleOrDefaultAsync(spec, cancellationToken);

            if (device is null)
            {
                return Result.Failure($"设备寻址失败：未找到寻址码为 [{code}] 的设备。");
            }

            identity = new CachedDeviceBootstrapIdentity(
                device.Id,
                device.DeviceName,
                code,
                device.ProcessId);

            await cacheService.SetAsync(cacheKey, identity, TimeSpan.FromHours(2), cancellationToken);
        }

        return await IssueSessionAsync(identity, cancellationToken);
    }

    private async Task<Result<BootstrapDeviceSessionResult>> HandleWithSecretAsync(
        string code,
        string? bootstrapSecret,
        CancellationToken cancellationToken)
    {
        var spec = new DeviceByCodeSpec(code);
        var device = await deviceRepository.GetSingleOrDefaultAsync(spec, cancellationToken);

        if (device is null)
        {
            return Result.Failure($"设备寻址失败：未找到寻址码为 [{code}] 的设备。");
        }

        if (!BootstrapSecretHasher.Verify(bootstrapSecret, device.BootstrapSecretHash))
        {
            return Result.Unauthorized("设备启动认证失败：启动密钥无效。");
        }

        return await IssueSessionAsync(
            new CachedDeviceBootstrapIdentity(
                device.Id,
                device.DeviceName,
                device.Code,
                device.ProcessId),
            cancellationToken);
    }

    private async Task<Result<BootstrapDeviceSessionResult>> IssueSessionAsync(
        CachedDeviceBootstrapIdentity identity,
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
