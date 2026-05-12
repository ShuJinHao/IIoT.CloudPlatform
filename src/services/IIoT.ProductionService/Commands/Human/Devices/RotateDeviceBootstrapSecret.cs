using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.ProductionService.Security;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Caching;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Devices;

[AuthorizeRequirement("Device.Update")]
public record RotateDeviceBootstrapSecretCommand(Guid DeviceId)
    : IHumanCommand<Result<RotateDeviceBootstrapSecretResultDto>>;

public sealed record RotateDeviceBootstrapSecretResultDto(
    Guid Id,
    string Code,
    string BootstrapSecret);

public class RotateDeviceBootstrapSecretHandler(
    ICurrentUser currentUser,
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IRepository<Device> deviceRepository,
    ICacheService cacheService,
    IAuditTrailService auditTrailService)
    : ICommandHandler<RotateDeviceBootstrapSecretCommand, Result<RotateDeviceBootstrapSecretResultDto>>
{
    public async Task<Result<RotateDeviceBootstrapSecretResultDto>> Handle(
        RotateDeviceBootstrapSecretCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUserDeviceAccessService.IsAdministrator)
        {
            return await FailAsync(
                request.DeviceId.ToString(),
                "只有管理员可以轮换设备启动密钥。",
                cancellationToken,
                forbidden: true);
        }

        var device = await deviceRepository.GetSingleOrDefaultAsync(
            new DeviceByIdSpec(request.DeviceId),
            cancellationToken);

        if (device is null)
        {
            return await FailAsync(
                request.DeviceId.ToString(),
                "目标设备不存在",
                cancellationToken);
        }

        var bootstrapSecret = BootstrapSecretGenerator.Generate();
        device.SetBootstrapSecretHash(BootstrapSecretHasher.Hash(bootstrapSecret));
        deviceRepository.Update(device);

        var affected = await deviceRepository.SaveChangesAsync(cancellationToken);
        if (affected <= 0)
        {
            return await FailAsync(
                device.Id.ToString(),
                "保存设备启动密钥失败。",
                cancellationToken);
        }

        await cacheService.RemoveAsync(CacheKeys.DeviceCode(device.Code), cancellationToken);

        await auditTrailService.TryWriteAsync(
            new AuditTrailEntry(
                ParseActorUserId(currentUser.Id),
                currentUser.UserName,
                "Device.RotateBootstrapSecret",
                "Device",
                device.Id.ToString(),
                DateTime.UtcNow,
                true,
                $"轮换设备 {device.DeviceName}（{device.Code}）的启动密钥。",
                null),
            cancellationToken);

        return Result.Success(new RotateDeviceBootstrapSecretResultDto(
            device.Id,
            device.Code,
            bootstrapSecret));
    }

    private async Task<Result<RotateDeviceBootstrapSecretResultDto>> FailAsync(
        string targetIdOrKey,
        string message,
        CancellationToken cancellationToken,
        bool forbidden = false)
    {
        await auditTrailService.TryWriteAsync(
            new AuditTrailEntry(
                ParseActorUserId(currentUser.Id),
                currentUser.UserName,
                "Device.RotateBootstrapSecret",
                "Device",
                targetIdOrKey,
                DateTime.UtcNow,
                false,
                $"轮换设备 {targetIdOrKey} 的启动密钥。",
                message),
            cancellationToken);

        return forbidden
            ? Result.Forbidden(message)
            : Result.Failure(message);
    }

    private static Guid? ParseActorUserId(string? rawUserId)
    {
        return Guid.TryParse(rawUserId, out var actorUserId)
            ? actorUserId
            : null;
    }
}
