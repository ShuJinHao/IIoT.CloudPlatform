using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Persistence;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Persistence;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.ClientVersions;

[DistributedLock("iiot:lock:device-report:{DeviceId}", TimeoutSeconds = 5)]
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
    string? RemoteIpAddress = null)
    : IDeviceCommand<Result<DeviceRuntimeHeartbeatResultDto>>;

public sealed class ReportDeviceRuntimeHeartbeatHandler(
    IDeviceIdentityQueryService deviceIdentityQueryService,
    IDeviceClientStateStore clientStateStore,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork,
    IDeviceReportWriteObservationReader observationReader)
    : ICommandHandler<ReportDeviceRuntimeHeartbeatCommand, Result<DeviceRuntimeHeartbeatResultDto>>
{
    public async Task<Result<DeviceRuntimeHeartbeatResultDto>> Handle(
        ReportDeviceRuntimeHeartbeatCommand request,
        CancellationToken cancellationToken)
    {
        var clientCode = request.ClientCode?.Trim().ToUpperInvariant()
                         ?? string.Empty;
        var receivedAtExactUtc = NormalizeExactUtc(
            timeProvider.GetUtcNow().UtcDateTime);
        var startedAtExactUtc = NormalizeExactUtc(
            request.StartedAtUtc);
        var reportedAtExactUtc = NormalizeExactUtc(
            request.ReportedAtUtc);
        if (startedAtExactUtc > reportedAtExactUtc)
        {
            return Result.Invalid(
                "运行心跳开始时间不能晚于上报时间。");
        }

        if (reportedAtExactUtc > receivedAtExactUtc.Add(
                DeviceClientSoftwareStatusResolver.MaximumFutureClockSkew))
        {
            return Result.Invalid(
                "运行心跳上报时间超出允许的未来时钟偏差。");
        }

        var receivedAtUtc = TruncateToPostgresMicrosecond(
            receivedAtExactUtc);
        var startedAtUtc = TruncateToPostgresMicrosecond(
            startedAtExactUtc);
        var reportedAtUtc = TruncateToPostgresMicrosecond(
            reportedAtExactUtc);
        var heartbeatId = Guid.NewGuid();
        var stateId = Guid.NewGuid();
        var targetHeartbeat = new EdgeDeviceRuntimeHeartbeat(
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
            receivedAtUtc,
            heartbeatId);
        var target = new DeviceReportState(
            targetHeartbeat.LastHeartbeatAtUtc,
            targetHeartbeat.UpdatedAtUtc,
            targetHeartbeat.GetContentSha256());
        DeviceReportState? baseline = null;
        var commitAttempted = false;
        try
        {
            return await unitOfWork.ExecuteResilientAsync(
                ExecuteTransactionAsync,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested
                  && !commitAttempted)
        {
            throw;
        }
        catch (CloudWriteException)
        {
            throw;
        }
        catch (Exception) when (commitAttempted)
        {
            return await ResolveCommitAsync();
        }

        async Task<Result<DeviceRuntimeHeartbeatResultDto>>
            ExecuteTransactionAsync(CancellationToken callbackToken)
        {
            var current = await ObserveAttemptAsync(callbackToken);
            var validation = ValidateIdentityAndSequence(current);
            if (!validation.IsSuccess)
            {
                return Result.From(validation);
            }

            if (current.RuntimeHeartbeat == target)
            {
                return Success();
            }

            baseline ??= current.RuntimeHeartbeat;
            if (current.RuntimeHeartbeat != baseline)
            {
                throw new CloudWriteConflictException();
            }

            await unitOfWork.BeginTransactionAsync(callbackToken);
            var identity =
                await deviceIdentityQueryService.GetByDeviceIdAsync(
                    request.DeviceId,
                    callbackToken);
            if (identity is null
                || !string.Equals(
                    identity.Code,
                    clientCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                await unitOfWork.RollbackAsync(callbackToken);
                throw new CloudWriteConflictException();
            }

            var heartbeat =
                await clientStateStore.GetRuntimeHeartbeatByIdentityAsync(
                    request.DeviceId,
                    clientCode,
                    callbackToken);
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
                    receivedAtUtc,
                    heartbeatId);
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
                    receivedAtUtc);
                if (updateResult
                    == RuntimeHeartbeatReportUpdateResult.Stale)
                {
                    await unitOfWork.RollbackAsync(callbackToken);
                    return Result.Invalid(
                        "运行心跳上报时间早于当前已接受心跳。");
                }

                if (updateResult
                    == RuntimeHeartbeatReportUpdateResult.Conflict)
                {
                    await unitOfWork.RollbackAsync(callbackToken);
                    throw new CloudWriteConflictException();
                }

                if (updateResult
                    == RuntimeHeartbeatReportUpdateResult.Idempotent)
                {
                    await unitOfWork.RollbackAsync(callbackToken);
                    return Success();
                }
            }

            var state = await clientStateStore.GetStateByIdentityAsync(
                request.DeviceId,
                clientCode,
                callbackToken);
            if (state is null)
            {
                state = new DeviceClientState(
                    request.DeviceId,
                    clientCode,
                    stateId,
                    receivedAtUtc);
                clientStateStore.AddState(state);
            }

            state.ApplyRuntimeHeartbeat(heartbeat);
            await clientStateStore.SaveChangesAsync(callbackToken);
            commitAttempted = true;
            await unitOfWork.CommitAsync(callbackToken);
            return Success();
        }

        async Task<Result<DeviceRuntimeHeartbeatResultDto>>
            ResolveCommitAsync()
        {
            var current = await CloudWriteCommitRecovery.TryObserveCommitAsync(
                token => observationReader.ObserveReportAsync(
                    request.DeviceId,
                    clientCode,
                    token));
            if (current is null
                || current.RuntimeHeartbeat == baseline)
            {
                throw new CloudWriteCommitUnknownException();
            }

            if (current.RuntimeHeartbeat == target)
            {
                return Success();
            }

            throw new CloudWriteConflictException();
        }

        async Task<DeviceReportWriteObservation> ObserveAttemptAsync(
            CancellationToken callbackToken)
        {
            var current = await CloudWriteCommitRecovery.TryObserveAttemptAsync(
                token => observationReader.ObserveReportAsync(
                    request.DeviceId,
                    clientCode,
                    token),
                callbackToken);
            return current ?? throw new CloudWriteCommitUnknownException();
        }

        Result ValidateIdentityAndSequence(
            DeviceReportWriteObservation current)
        {
            if (!current.DeviceExists)
                return Result.Failure(
                    "运行心跳上报失败: 设备不存在");
            if (!string.Equals(
                    current.ClientCode,
                    clientCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure(
                    "运行心跳上报失败: ClientCode 与 DeviceId 不匹配");
            }

            if (current.RuntimeHeartbeat is not null
                && reportedAtUtc
                < current.RuntimeHeartbeat.ReportedAtUtc)
            {
                return Result.Invalid(
                    "运行心跳上报时间早于当前已接受心跳。");
            }

            if (current.RuntimeHeartbeat is not null
                && reportedAtUtc
                == current.RuntimeHeartbeat.ReportedAtUtc
                && !string.Equals(
                    current.RuntimeHeartbeat.ContentSha256,
                    target.ContentSha256,
                    StringComparison.Ordinal))
            {
                throw new CloudWriteConflictException();
            }

            return Result.Success();
        }

        Result<DeviceRuntimeHeartbeatResultDto> Success()
            => Result.Success(new DeviceRuntimeHeartbeatResultDto(
                request.DeviceId,
                reportedAtUtc));
    }

    private static DateTime NormalizeExactUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

    private static DateTime TruncateToPostgresMicrosecond(
        DateTime utc)
        => new(
            utc.Ticks - utc.Ticks % 10,
            DateTimeKind.Utc);
}
