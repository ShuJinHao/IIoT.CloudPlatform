using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.ProductionService.Queries.Devices;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Bootstrap.Devices;

public record RefreshEdgeDeviceIdentityCommand(string RefreshToken)
    : IAnonymousBootstrapCommand<Result<BootstrapDeviceSessionResult>>;

public sealed class RefreshEdgeDeviceIdentityHandler(
    IReadRepository<Device> deviceRepository,
    IJwtTokenGenerator jwtTokenGenerator,
    IRefreshTokenService refreshTokenService)
    : ICommandHandler<RefreshEdgeDeviceIdentityCommand, Result<BootstrapDeviceSessionResult>>
{
    public async Task<Result<BootstrapDeviceSessionResult>> Handle(
        RefreshEdgeDeviceIdentityCommand request,
        CancellationToken cancellationToken)
    {
        var rotationResult = await refreshTokenService.RotateAsync(
            IIoTClaimTypes.EdgeDeviceActor,
            request.RefreshToken,
            cancellationToken);

        if (!rotationResult.IsSuccess)
        {
            return Result.Unauthorized(rotationResult.Errors?.ToArray() ?? ["Refresh token is invalid or expired."]);
        }

        var device = await deviceRepository.GetSingleOrDefaultAsync(
            new DeviceByIdSpec(rotationResult.Value!.SubjectId),
            cancellationToken);

        if (device is null)
        {
            await refreshTokenService.RevokeSubjectTokensAsync(
                IIoTClaimTypes.EdgeDeviceActor,
                rotationResult.Value.SubjectId,
                "device-unavailable",
                cancellationToken);

            return Result.Unauthorized("Device is unavailable.");
        }

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
                accessToken.ExpiresAtUtc),
            rotationResult.Value.RefreshToken.Token,
            rotationResult.Value.RefreshToken.ExpiresAtUtc));
    }
}
