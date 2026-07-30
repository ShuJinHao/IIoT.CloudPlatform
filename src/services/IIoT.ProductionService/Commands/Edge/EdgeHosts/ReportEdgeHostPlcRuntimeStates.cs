using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.EdgeHosts;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Core.Production.Contracts.EdgeHosts;
using IIoT.ProductionService.ClientReleases;
using IIoT.ProductionService.EdgeHosts;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Persistence;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Persistence;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.EdgeHosts;

public sealed record EdgeHostPlcRuntimeStateReportItem(
    string PlcCode,
    string? ReportedPlcName,
    bool IsConnected,
    string? RuntimeStatus = null,
    DateTime? ObservedAtUtc = null,
    string? StationCode = null,
    string? Protocol = null,
    string? Address = null,
    string? LastError = null);

[DistributedLock("iiot:lock:device-report:{DeviceId}", TimeoutSeconds = 5)]
public sealed record ReportEdgeHostPlcRuntimeStatesCommand(
    Guid DeviceId,
    string ClientCode,
    DateTime ReportedAtUtc,
    IReadOnlyList<EdgeHostPlcRuntimeStateReportItem> PlcStates)
    : IDeviceCommand<Result<EdgeHostPlcRuntimeStateReportResultDto>>;

public sealed class ReportEdgeHostPlcRuntimeStatesHandler(
    IDeviceIdentityQueryService deviceIdentityQueryService,
    IEdgeHostPlcRuntimeStateStore runtimeStateStore,
    IDeviceClientStateStore clientStateStore,
    IUnitOfWork unitOfWork,
    IDeviceReportWriteObservationReader observationReader,
    TimeProvider timeProvider)
    : ICommandHandler<ReportEdgeHostPlcRuntimeStatesCommand, Result<EdgeHostPlcRuntimeStateReportResultDto>>
{
    private const string LegacyPlcSnapshotContentMarker =
        "0000000000000000000000000000000000000000000000000000000000000000";

    public async Task<Result<EdgeHostPlcRuntimeStateReportResultDto>> Handle(
        ReportEdgeHostPlcRuntimeStatesCommand request,
        CancellationToken cancellationToken)
    {
        var clientCodeResult = NormalizeClientCode(request.ClientCode);
        if (!clientCodeResult.IsSuccess)
        {
            return Result.From(clientCodeResult);
        }

        var clientCode = clientCodeResult.Value!;
        var reportedAtUtc = NormalizeUtc(request.ReportedAtUtc);
        var receivedAtUtc = NormalizeUtc(
            timeProvider.GetUtcNow().UtcDateTime);
        if (reportedAtUtc > receivedAtUtc.Add(
                DeviceClientSoftwareStatusResolver.MaximumFutureClockSkew))
        {
            return Result.Invalid(
                "PLC 状态上报时间超出允许的未来时钟偏差。");
        }

        var normalizedReports = NormalizeReports(
            request,
            receivedAtUtc,
            out var invalidMessage);
        if (invalidMessage is not null)
        {
            return Result.Invalid(invalidMessage);
        }

        var targetHash = EdgeHostPlcRuntimeSnapshotFingerprint.Compute(
            normalizedReports.Select(report => report.Content));
        var target = new DeviceReportState(
            reportedAtUtc,
            receivedAtUtc,
            targetHash);
        var stateId = Guid.NewGuid();
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

        async Task<Result<EdgeHostPlcRuntimeStateReportResultDto>>
            ExecuteTransactionAsync(CancellationToken callbackToken)
        {
            var current = await ObserveAttemptAsync(callbackToken);
            var validation = ValidateIdentityAndSequence(current);
            if (!validation.IsSuccess)
            {
                return Result.From(validation);
            }

            if (MatchesSameReport(current.PlcSnapshot, target))
            {
                return Success();
            }

            baseline ??= current.PlcSnapshot;
            if (current.PlcSnapshot != baseline)
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

            var state = await clientStateStore.GetStateByIdentityAsync(
                request.DeviceId,
                clientCode,
                callbackToken);
            var existingStates =
                await runtimeStateStore.GetByIdentityAsync(
                    request.DeviceId,
                    clientCode,
                    callbackToken);
            var trackedMarker = ToReportState(
                state,
                existingStates);
            if (trackedMarker != baseline)
            {
                await unitOfWork.RollbackAsync(callbackToken);
                throw new CloudWriteConflictException();
            }

            if (state is null)
            {
                state = new DeviceClientState(
                    request.DeviceId,
                    clientCode,
                    stateId,
                    receivedAtUtc);
                clientStateStore.AddState(state);
            }

            var statesByPlcCode = existingStates.ToDictionary(
                item => item.PlcCode,
                StringComparer.OrdinalIgnoreCase);
            var reportedPlcCodes = normalizedReports
                .Select(report => report.Content.PlcCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var report in normalizedReports)
            {
                if (!statesByPlcCode.TryGetValue(
                        report.Content.PlcCode,
                        out var runtimeState))
                {
                    runtimeState = new EdgeHostPlcRuntimeState(
                        request.DeviceId,
                        clientCode,
                        report.Content.PlcCode,
                        report.Id,
                        receivedAtUtc);
                    runtimeStateStore.Add(runtimeState);
                    statesByPlcCode[report.Content.PlcCode] =
                        runtimeState;
                }

                runtimeState.ReplaceReport(
                    report.Content.ReportedPlcName,
                    report.Content.IsConnected,
                    report.Content.RuntimeStatus,
                    report.Content.ObservedAtUtc,
                    report.Content.StationCode,
                    report.Content.Protocol,
                    report.Content.Address,
                    report.Content.LastError);
            }

            foreach (var missingState in existingStates.Where(
                         existing => !reportedPlcCodes.Contains(
                             existing.PlcCode)))
            {
                runtimeStateStore.Delete(missingState);
            }

            state.ApplyPlcSnapshot(
                reportedAtUtc,
                receivedAtUtc,
                targetHash);
            await runtimeStateStore.SaveChangesAsync(callbackToken);
            commitAttempted = true;
            await unitOfWork.CommitAsync(callbackToken);
            return Success();
        }

        async Task<Result<EdgeHostPlcRuntimeStateReportResultDto>>
            ResolveCommitAsync()
        {
            var current = await CloudWriteCommitRecovery.TryObserveCommitAsync(
                token => observationReader.ObserveReportAsync(
                    request.DeviceId,
                    clientCode,
                    token));
            if (current is null
                || current.PlcSnapshot == baseline)
            {
                throw new CloudWriteCommitUnknownException();
            }

            if (MatchesSameReport(
                    current.PlcSnapshot,
                    target))
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
                    "PLC 状态上报失败: 设备不存在");
            if (!string.Equals(
                    current.ClientCode,
                    clientCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure(
                    "PLC 状态上报失败: ClientCode 与 DeviceId 不匹配");
            }

            if (current.PlcSnapshot is not null
                && reportedAtUtc < current.PlcSnapshot.ReportedAtUtc)
            {
                return Result.Invalid(
                    "PLC 状态上报时间早于当前已接受快照。");
            }

            if (current.PlcSnapshot is not null
                && reportedAtUtc == current.PlcSnapshot.ReportedAtUtc
                && !string.Equals(
                    current.PlcSnapshot.ContentSha256,
                    targetHash,
                    StringComparison.Ordinal))
            {
                throw new CloudWriteConflictException();
            }

            return Result.Success();
        }

        Result<EdgeHostPlcRuntimeStateReportResultDto> Success()
            => Result.Success(
                new EdgeHostPlcRuntimeStateReportResultDto(
                    request.DeviceId,
                    clientCode,
                    normalizedReports.Count,
                    reportedAtUtc));
    }

    private static Result<string> NormalizeClientCode(string clientCode)
    {
        try
        {
            return Result.Success(
                EdgeHostPlcRuntimeState.NormalizeClientCode(clientCode));
        }
        catch (ArgumentException ex)
        {
            return Result.Invalid(ex.Message);
        }
    }

    private static List<NormalizedPlcRuntimeStateReport> NormalizeReports(
        ReportEdgeHostPlcRuntimeStatesCommand request,
        DateTime receivedAtUtc,
        out string? invalidMessage)
    {
        invalidMessage = null;
        var seenCodes = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var reports = new List<NormalizedPlcRuntimeStateReport>();
        foreach (var item in request.PlcStates ?? [])
        {
            try
            {
                var plcCode =
                    EdgeHostPlcRuntimeState.NormalizePlcCode(item.PlcCode);
                if (!seenCodes.Add(plcCode))
                {
                    invalidMessage =
                        "同一次 PLC 状态上报不能包含重复 PLC 编码。";
                    return [];
                }

                var id = Guid.NewGuid();
                var normalizedState = new EdgeHostPlcRuntimeState(
                    request.DeviceId,
                    request.ClientCode,
                    plcCode,
                    id,
                    receivedAtUtc);
                normalizedState.ReplaceReport(
                    item.ReportedPlcName,
                    item.IsConnected,
                    item.RuntimeStatus,
                    item.ObservedAtUtc.HasValue
                        ? NormalizeUtc(item.ObservedAtUtc.Value)
                        : NormalizeUtc(request.ReportedAtUtc),
                    item.StationCode,
                    item.Protocol,
                    item.Address,
                    item.LastError);
                reports.Add(new NormalizedPlcRuntimeStateReport(
                    id,
                    ToContent(normalizedState)));
            }
            catch (ArgumentException ex)
            {
                invalidMessage = ex.Message;
                return [];
            }
        }

        return reports
            .OrderBy(report => report.Content.PlcCode)
            .ToList();
    }

    private static EdgeHostPlcRuntimeSnapshotContent ToContent(
        EdgeHostPlcRuntimeState state)
        => new(
            state.PlcCode,
            state.ReportedPlcName,
            state.IsConnected,
            state.RuntimeStatus,
            state.LastSeenAtUtc,
            state.StationCode,
            state.Protocol,
            state.Address,
            state.LastError);

    private static DeviceReportState? ToReportState(
        DeviceClientState? state,
        IReadOnlyCollection<EdgeHostPlcRuntimeState> runtimeStates)
        => state?.PlcSnapshotReportedAtUtc is null
           || state.PlcSnapshotReceivedAtUtc is null
           || string.IsNullOrWhiteSpace(
               state.PlcSnapshotContentSha256)
            ? null
            : new DeviceReportState(
                state.PlcSnapshotReportedAtUtc.Value,
                state.PlcSnapshotReceivedAtUtc.Value,
                string.Equals(
                    state.PlcSnapshotContentSha256,
                    LegacyPlcSnapshotContentMarker,
                    StringComparison.Ordinal)
                    ? EdgeHostPlcRuntimeSnapshotFingerprint.Compute(
                        runtimeStates.Select(ToContent))
                    : state.PlcSnapshotContentSha256);

    private static bool MatchesSameReport(
        DeviceReportState? current,
        DeviceReportState expected)
        => current is not null
           && current.ReportedAtUtc == expected.ReportedAtUtc
           && string.Equals(
               current.ContentSha256,
               expected.ContentSha256,
               StringComparison.Ordinal);

    private static DateTime NormalizeUtc(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
        return new DateTime(
            utc.Ticks - utc.Ticks % 10,
            DateTimeKind.Utc);
    }

    private sealed record NormalizedPlcRuntimeStateReport(
        Guid Id,
        EdgeHostPlcRuntimeSnapshotContent Content);
}
