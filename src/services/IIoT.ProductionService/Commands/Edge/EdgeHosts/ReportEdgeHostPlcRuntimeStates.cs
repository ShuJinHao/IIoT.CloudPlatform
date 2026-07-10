using IIoT.Core.Production.Aggregates.EdgeHosts;
using IIoT.Core.Production.Contracts.EdgeHosts;
using IIoT.ProductionService.EdgeHosts;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.RecordQueries;
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

public sealed record ReportEdgeHostPlcRuntimeStatesCommand(
    Guid DeviceId,
    string ClientCode,
    DateTime ReportedAtUtc,
    IReadOnlyList<EdgeHostPlcRuntimeStateReportItem> PlcStates)
    : IDeviceCommand<Result<EdgeHostPlcRuntimeStateReportResultDto>>;

public sealed class ReportEdgeHostPlcRuntimeStatesHandler(
    IDeviceIdentityQueryService deviceIdentityQueryService,
    IEdgeHostPlcRuntimeStateStore runtimeStateStore)
    : ICommandHandler<ReportEdgeHostPlcRuntimeStatesCommand, Result<EdgeHostPlcRuntimeStateReportResultDto>>
{
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
        var identity = await deviceIdentityQueryService.GetByDeviceIdAsync(
            request.DeviceId,
            cancellationToken);
        if (identity is null)
        {
            return Result.Failure("PLC 状态上报失败: 设备不存在");
        }

        if (!string.Equals(identity.Code, clientCode, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure("PLC 状态上报失败: ClientCode 与 DeviceId 不匹配");
        }

        var normalizedReports = NormalizeReports(request, out var invalidMessage);
        if (invalidMessage is not null)
        {
            return Result.Invalid(invalidMessage);
        }

        var existingStates = await runtimeStateStore.GetByIdentityAsync(
                request.DeviceId,
                clientCode,
                cancellationToken);
        var statesByPlcCode = existingStates
            .ToDictionary(state => state.PlcCode, StringComparer.OrdinalIgnoreCase);
        var reportedPlcCodes = normalizedReports
            .Select(static report => report.PlcCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var report in normalizedReports)
        {
            if (!statesByPlcCode.TryGetValue(report.PlcCode, out var state))
            {
                state = new EdgeHostPlcRuntimeState(
                    request.DeviceId,
                    clientCode,
                    report.PlcCode);
                runtimeStateStore.Add(state);
                statesByPlcCode[report.PlcCode] = state;
            }

            state.ReplaceReport(
                report.ReportedPlcName,
                report.IsConnected,
                report.RuntimeStatus,
                report.ObservedAtUtc,
                report.StationCode,
                report.Protocol,
                report.Address,
                report.LastError);
        }

        foreach (var missingState in existingStates.Where(state => !reportedPlcCodes.Contains(state.PlcCode)))
        {
            runtimeStateStore.Delete(missingState);
        }

        await runtimeStateStore.SaveChangesAsync(cancellationToken);
        return Result.Success(new EdgeHostPlcRuntimeStateReportResultDto(
            request.DeviceId,
            clientCode,
            normalizedReports.Count,
            NormalizeUtc(request.ReportedAtUtc)));
    }

    private static Result<string> NormalizeClientCode(string clientCode)
    {
        try
        {
            return Result.Success(EdgeHostPlcRuntimeState.NormalizeClientCode(clientCode));
        }
        catch (ArgumentException ex)
        {
            return Result.Invalid(ex.Message);
        }
    }

    private static List<NormalizedPlcRuntimeStateReport> NormalizeReports(
        ReportEdgeHostPlcRuntimeStatesCommand request,
        out string? invalidMessage)
    {
        invalidMessage = null;
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reports = new List<NormalizedPlcRuntimeStateReport>();
        foreach (var item in request.PlcStates ?? [])
        {
            string plcCode;
            try
            {
                plcCode = EdgeHostPlcRuntimeState.NormalizePlcCode(item.PlcCode);
            }
            catch (ArgumentException ex)
            {
                invalidMessage = ex.Message;
                return [];
            }

            if (!seenCodes.Add(plcCode))
            {
                invalidMessage = "同一次 PLC 状态上报不能包含重复 PLC 编码。";
                return [];
            }

            reports.Add(new NormalizedPlcRuntimeStateReport(
                plcCode,
                item.ReportedPlcName,
                item.IsConnected,
                item.RuntimeStatus,
                item.ObservedAtUtc.HasValue
                    ? NormalizeUtc(item.ObservedAtUtc.Value)
                    : NormalizeUtc(request.ReportedAtUtc),
                item.StationCode,
                item.Protocol,
                item.Address,
                item.LastError));
        }

        return reports;
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

    private sealed record NormalizedPlcRuntimeStateReport(
        string PlcCode,
        string? ReportedPlcName,
        bool IsConnected,
        string? RuntimeStatus,
        DateTime ObservedAtUtc,
        string? StationCode,
        string? Protocol,
        string? Address,
        string? LastError);
}
