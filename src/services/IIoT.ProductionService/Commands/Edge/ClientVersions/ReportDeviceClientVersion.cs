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

public sealed record DeviceClientPluginVersionReportItem(
    string ModuleId,
    string? DisplayName,
    string Version,
    string? HostApiVersion);

[DistributedLock("iiot:lock:device-report:{DeviceId}", TimeoutSeconds = 5)]
public sealed record ReportDeviceClientVersionCommand(
    Guid DeviceId,
    string ClientCode,
    string HostVersion,
    string HostApiVersion,
    IReadOnlyList<DeviceClientPluginVersionReportItem> InstalledPlugins,
    IReadOnlyList<string> EnabledPlugins,
    string Channel,
    DateTime ReportedAtUtc,
    IReadOnlyList<string>? LocalIpAddresses = null,
    string? RemoteIpAddress = null)
    : IDeviceCommand<Result<DeviceClientVersionReportResultDto>>;

public sealed class ReportDeviceClientVersionHandler(
    IDeviceIdentityQueryService deviceIdentityQueryService,
    IDeviceClientStateStore clientStateStore,
    IUnitOfWork unitOfWork,
    IDeviceReportWriteObservationReader observationReader,
    TimeProvider timeProvider)
    : ICommandHandler<ReportDeviceClientVersionCommand, Result<DeviceClientVersionReportResultDto>>
{
    public async Task<Result<DeviceClientVersionReportResultDto>> Handle(
        ReportDeviceClientVersionCommand request,
        CancellationToken cancellationToken)
    {
        var clientCode = request.ClientCode?.Trim().ToUpperInvariant()
                         ?? string.Empty;
        var reportedAtUtc = NormalizeUtc(request.ReportedAtUtc);
        var receivedAtUtc = NormalizeUtc(
            timeProvider.GetUtcNow().UtcDateTime);
        var stateId = Guid.NewGuid();
        var enabled = new HashSet<string>(
            request.EnabledPlugins ?? [],
            StringComparer.OrdinalIgnoreCase);
        var pluginVersions = (request.InstalledPlugins ?? [])
            .GroupBy(
                plugin => plugin.ModuleId.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var plugin = group.First();
                return new DeviceClientPluginVersion(
                    plugin.ModuleId,
                    plugin.DisplayName,
                    plugin.Version,
                    plugin.HostApiVersion,
                    enabled.Contains(plugin.ModuleId));
            })
            .ToArray();
        var targetSnapshot = new DeviceClientVersionSnapshot(
            request.DeviceId,
            clientCode,
            request.HostVersion,
            request.HostApiVersion,
            request.Channel,
            reportedAtUtc,
            pluginVersions,
            request.LocalIpAddresses,
            request.RemoteIpAddress,
            receivedAtUtc);
        var target = new DeviceReportState(
            targetSnapshot.ReportedAtUtc,
            targetSnapshot.ReceivedAtUtc,
            targetSnapshot.GetContentSha256());
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

        async Task<Result<DeviceClientVersionReportResultDto>>
            ExecuteTransactionAsync(CancellationToken callbackToken)
        {
            var current = await ObserveAttemptAsync(callbackToken);
            var validation = ValidateIdentityAndSequence(current);
            if (!validation.IsSuccess)
            {
                return Result.From(validation);
            }

            if (MatchesSameReport(current.Version, target))
            {
                return Success(current.Version!.ReceivedAtUtc);
            }

            baseline ??= current.Version;
            if (current.Version != baseline)
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

            var snapshot =
                await clientStateStore.GetVersionSnapshotByDeviceAsync(
                    request.DeviceId,
                    callbackToken);
            if (snapshot is null)
            {
                snapshot = new DeviceClientVersionSnapshot(
                    request.DeviceId,
                    clientCode,
                    request.HostVersion,
                    request.HostApiVersion,
                    request.Channel,
                    reportedAtUtc,
                    pluginVersions.Select(ClonePlugin),
                    request.LocalIpAddresses,
                    request.RemoteIpAddress,
                    receivedAtUtc);
                clientStateStore.AddVersionSnapshot(snapshot);
            }
            else
            {
                var updateResult = snapshot.ReplaceReport(
                    clientCode,
                    request.HostVersion,
                    request.HostApiVersion,
                    request.Channel,
                    reportedAtUtc,
                    pluginVersions.Select(ClonePlugin),
                    request.LocalIpAddresses,
                    request.RemoteIpAddress,
                    receivedAtUtc);
                if (updateResult
                    == DeviceClientVersionReportUpdateResult.Stale)
                {
                    await unitOfWork.RollbackAsync(callbackToken);
                    return Result.Invalid(
                        "版本上报时间早于当前已接受版本。");
                }

                if (updateResult
                    == DeviceClientVersionReportUpdateResult.Conflict)
                {
                    await unitOfWork.RollbackAsync(callbackToken);
                    throw new CloudWriteConflictException();
                }

                if (updateResult
                    == DeviceClientVersionReportUpdateResult.Idempotent)
                {
                    await unitOfWork.RollbackAsync(callbackToken);
                    return Success(snapshot.ReceivedAtUtc);
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

            state.ApplyVersionReport(snapshot);
            await clientStateStore.SaveChangesAsync(callbackToken);
            commitAttempted = true;
            await unitOfWork.CommitAsync(callbackToken);
            return Success(receivedAtUtc);
        }

        async Task<Result<DeviceClientVersionReportResultDto>>
            ResolveCommitAsync()
        {
            var current = await CloudWriteCommitRecovery.TryObserveCommitAsync(
                token => observationReader.ObserveReportAsync(
                    request.DeviceId,
                    clientCode,
                    token));
            if (current is null
                || current.Version == baseline)
            {
                throw new CloudWriteCommitUnknownException();
            }

            if (MatchesSameReport(current.Version, target))
            {
                return Success(current.Version!.ReceivedAtUtc);
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
                return Result.Failure("版本上报失败: 设备不存在");
            if (!string.Equals(
                    current.ClientCode,
                    clientCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure(
                    "版本上报失败: ClientCode 与 DeviceId 不匹配");
            }

            if (current.Version is not null
                && reportedAtUtc < current.Version.ReportedAtUtc)
            {
                return Result.Invalid(
                    "版本上报时间早于当前已接受版本。");
            }

            if (current.Version is not null
                && reportedAtUtc == current.Version.ReportedAtUtc
                && !string.Equals(
                    current.Version.ContentSha256,
                    target.ContentSha256,
                    StringComparison.Ordinal))
            {
                throw new CloudWriteConflictException();
            }

            return Result.Success();
        }

        Result<DeviceClientVersionReportResultDto> Success(
            DateTime acceptedAtUtc)
            => Result.Success(new DeviceClientVersionReportResultDto(
                request.DeviceId,
                acceptedAtUtc));
    }

    private static bool MatchesSameReport(
        DeviceReportState? current,
        DeviceReportState expected)
        => current is not null
           && current.ReportedAtUtc == expected.ReportedAtUtc
           && string.Equals(
               current.ContentSha256,
               expected.ContentSha256,
               StringComparison.Ordinal);

    private static DeviceClientPluginVersion ClonePlugin(
        DeviceClientPluginVersion plugin)
        => new(
            plugin.ModuleId,
            plugin.DisplayName,
            plugin.Version,
            plugin.HostApiVersion,
            plugin.Enabled);

    private static DateTime NormalizeUtc(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
        return new DateTime(
            utc.Ticks - utc.Ticks % 10,
            DateTimeKind.Utc);
    }
}
