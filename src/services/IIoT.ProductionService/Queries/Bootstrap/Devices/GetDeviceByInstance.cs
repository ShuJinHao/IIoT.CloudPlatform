using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.Identity;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Devices;

file sealed record CachedDeviceBootstrapIdentity(
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

public record GetDeviceByInstanceQuery(
    string Code
) : IAnonymousBootstrapQuery<Result<DeviceIdentityDto>>;

public class GetDeviceByInstanceHandler(
    IReadRepository<Device> deviceRepository,
    ICacheService cacheService,
    IJwtTokenGenerator jwtTokenGenerator
) : IQueryHandler<GetDeviceByInstanceQuery, Result<DeviceIdentityDto>>
{
    public async Task<Result<DeviceIdentityDto>> Handle(
        GetDeviceByInstanceQuery request,
        CancellationToken cancellationToken)
    {
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure("Bootstrap 查询失败：设备 Code 不能为空。");
        }

        var cacheKey = CacheKeys.DeviceCode(code);
        var identity = await cacheService.GetAsync<CachedDeviceBootstrapIdentity>(cacheKey, cancellationToken);
        if (identity is null)
        {
            var spec = new DeviceByCodeSpec(code);
            var device = await deviceRepository.GetSingleOrDefaultAsync(spec, cancellationToken);

            if (device is null)
            {
                return Result.Failure($"Bootstrap 查询失败：未找到 Code 为 [{code}] 的设备。");
            }

            identity = new CachedDeviceBootstrapIdentity(
                device.Id,
                device.DeviceName,
                code,
                device.ProcessId);

            await cacheService.SetAsync(cacheKey, identity, TimeSpan.FromHours(2), cancellationToken);
        }

        var token = jwtTokenGenerator.GenerateEdgeDeviceToken(
            identity.Id,
            identity.ClientCode,
            identity.ProcessId);

        return Result.Success(new DeviceIdentityDto(
            identity.Id,
            identity.DeviceName,
            identity.ClientCode,
            identity.ProcessId,
            token.Token,
            token.ExpiresAtUtc));
    }
}
