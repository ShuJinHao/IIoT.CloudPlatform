using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.ClientVersions;

public sealed record ReportDeviceRuntimeHeartbeatCommand(
    Guid DeviceId,
    string ClientCode,
    string RuntimeInstanceId,
    string? MachineProfile,
    string HostVersion,
    string HostApiVersion,
    string Status,
    DateTime StartedAtUtc,
    DateTime ReportedAtUtc,
    IReadOnlyList<string>? LocalIpAddresses = null,
    string? RemoteIpAddress = null) : IDeviceCommand<Result<DeviceRuntimeHeartbeatResultDto>>;

public sealed class ReportDeviceRuntimeHeartbeatHandler(
    IDeviceIdentityQueryService deviceIdentityQueryService,
    IRepository<EdgeDeviceRuntimeHeartbeat> repository,
    IRepository<DeviceClientState> stateRepository)
    : ICommandHandler<ReportDeviceRuntimeHeartbeatCommand, Result<DeviceRuntimeHeartbeatResultDto>>
{
    public async Task<Result<DeviceRuntimeHeartbeatResultDto>> Handle(
        ReportDeviceRuntimeHeartbeatCommand request,
        CancellationToken cancellationToken)
    {
        var clientCode = request.ClientCode?.Trim() ?? string.Empty;
        var identity = await deviceIdentityQueryService.GetByDeviceIdAsync(
            request.DeviceId,
            cancellationToken);
        if (identity is null)
        {
            return Result.Failure("运行心跳上报失败: 设备不存在");
        }

        if (!string.Equals(identity.Code, clientCode, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure("运行心跳上报失败: ClientCode 与 DeviceId 不匹配");
        }

        var heartbeat = await repository.GetSingleOrDefaultAsync(
            new EdgeDeviceRuntimeHeartbeatByIdentitySpec(request.DeviceId, clientCode),
            cancellationToken);
        if (heartbeat is null)
        {
            heartbeat = new EdgeDeviceRuntimeHeartbeat(
                request.DeviceId,
                clientCode,
                request.RuntimeInstanceId,
                request.MachineProfile,
                request.HostVersion,
                request.HostApiVersion,
                request.Status,
                request.StartedAtUtc,
                request.ReportedAtUtc,
                request.LocalIpAddresses,
                request.RemoteIpAddress);
            repository.Add(heartbeat);
        }
        else
        {
            heartbeat.ReplaceReport(
                request.RuntimeInstanceId,
                request.MachineProfile,
                request.HostVersion,
                request.HostApiVersion,
                request.Status,
                request.StartedAtUtc,
                request.ReportedAtUtc,
                request.LocalIpAddresses,
                request.RemoteIpAddress);
        }

        var state = await stateRepository.GetSingleOrDefaultAsync(
            new DeviceClientStateByIdentitySpec(request.DeviceId, clientCode),
            cancellationToken);
        if (state is null)
        {
            state = new DeviceClientState(request.DeviceId, clientCode);
            stateRepository.Add(state);
        }

        state.ApplyRuntimeHeartbeat(heartbeat);

        await stateRepository.SaveChangesAsync(cancellationToken);
        return Result.Success(new DeviceRuntimeHeartbeatResultDto(
            heartbeat.DeviceId,
            heartbeat.LastHeartbeatAtUtc));
    }
}
