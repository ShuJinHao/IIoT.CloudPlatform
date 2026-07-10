using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.ClientVersions;

[DistributedLock("iiot:lock:device-runtime-heartbeat:{DeviceId}", TimeoutSeconds = 5)]
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
    IDeviceClientStateStore clientStateStore,
    TimeProvider timeProvider)
    : ICommandHandler<ReportDeviceRuntimeHeartbeatCommand, Result<DeviceRuntimeHeartbeatResultDto>>
{
    public async Task<Result<DeviceRuntimeHeartbeatResultDto>> Handle(
        ReportDeviceRuntimeHeartbeatCommand request,
        CancellationToken cancellationToken)
    {
        var clientCode = request.ClientCode?.Trim() ?? string.Empty;
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var startedAtUtc = NormalizeUtc(request.StartedAtUtc);
        var reportedAtUtc = NormalizeUtc(request.ReportedAtUtc);
        if (startedAtUtc > reportedAtUtc)
        {
            return Result.Invalid("运行心跳开始时间不能晚于上报时间。");
        }

        if (reportedAtUtc > utcNow.Add(DeviceClientSoftwareStatusResolver.MaximumFutureClockSkew))
        {
            return Result.Invalid("运行心跳上报时间超出允许的未来时钟偏差。");
        }

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

        var heartbeat = await clientStateStore.GetRuntimeHeartbeatByIdentityAsync(
            request.DeviceId,
            clientCode,
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
                startedAtUtc,
                reportedAtUtc,
                request.LocalIpAddresses,
                request.RemoteIpAddress,
                utcNow);
            clientStateStore.AddRuntimeHeartbeat(heartbeat);
        }
        else
        {
            var updateResult = heartbeat.ReplaceReport(
                request.RuntimeInstanceId,
                request.MachineProfile,
                request.HostVersion,
                request.HostApiVersion,
                request.Status,
                startedAtUtc,
                reportedAtUtc,
                request.LocalIpAddresses,
                request.RemoteIpAddress,
                utcNow);
            if (updateResult == RuntimeHeartbeatReportUpdateResult.Stale)
            {
                return Result.Invalid("运行心跳上报时间早于当前已接受心跳。");
            }

            if (updateResult == RuntimeHeartbeatReportUpdateResult.Conflict)
            {
                return Result.Invalid("相同运行心跳时间对应的上报内容不一致。");
            }

            if (updateResult == RuntimeHeartbeatReportUpdateResult.Idempotent)
            {
                return Result.Success(new DeviceRuntimeHeartbeatResultDto(
                    heartbeat.DeviceId,
                    heartbeat.LastHeartbeatAtUtc));
            }
        }

        var state = await clientStateStore.GetStateByIdentityAsync(request.DeviceId, clientCode, cancellationToken);
        if (state is null)
        {
            state = new DeviceClientState(request.DeviceId, clientCode);
            clientStateStore.AddState(state);
        }

        state.ApplyRuntimeHeartbeat(heartbeat);

        await clientStateStore.SaveChangesAsync(cancellationToken);
        return Result.Success(new DeviceRuntimeHeartbeatResultDto(
            heartbeat.DeviceId,
            heartbeat.LastHeartbeatAtUtc));
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
}
