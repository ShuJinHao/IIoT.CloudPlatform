using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.EdgeHosts;
using IIoT.ProductionService.ClientReleases;

namespace IIoT.ProductionService.EdgeHosts;

public sealed record EdgeHostListItemDto(
    Guid Id,
    Guid DeviceId,
    string ClientCode,
    string HostName,
    string? PrimaryIpAddress,
    IReadOnlyList<string> LocalIpAddresses,
    string SoftwareStatus,
    string? CurrentVersion,
    DateTime? LastRuntimeHeartbeatAtUtc,
    int PlcCount,
    int ConnectedPlcCount,
    int FaultedPlcCount,
    DateTime? LastPlcSeenAtUtc,
    string? Issue);

public sealed record EdgeHostDto(
    Guid Id,
    Guid DeviceId,
    string ClientCode,
    string HostName,
    string? PrimaryIpAddress,
    IReadOnlyList<string> LocalIpAddresses,
    string SoftwareStatus,
    string? CurrentVersion,
    DateTime? LastRuntimeHeartbeatAtUtc,
    int PlcCount,
    int ConnectedPlcCount,
    int FaultedPlcCount,
    DateTime? LastPlcSeenAtUtc,
    string? Issue,
    IReadOnlyList<EdgeHostPlcRuntimeStateDto> PlcStates);

public sealed record EdgeHostPlcRuntimeStateDto(
    Guid Id,
    Guid DeviceId,
    string ClientCode,
    string PlcCode,
    string? ReportedPlcName,
    string? RuntimeStationCode,
    string? RuntimeProtocol,
    string? RuntimeAddress,
    bool IsConnected,
    string RuntimeStatus,
    string? LastError,
    DateTime LastSeenAtUtc,
    DateTime UpdatedAtUtc);

public sealed record EdgeHostPlcRuntimeStateReportResultDto(
    Guid DeviceId,
    string ClientCode,
    int ReceivedCount,
    DateTime ReceivedAtUtc);

public static class EdgeHostMapping
{
    public static EdgeHostListItemDto ToListItemDto(
        Device device,
        DeviceClientState? clientState,
        IReadOnlyList<EdgeHostPlcRuntimeState> plcStates,
        DateTime utcNow)
        => ToListItemDto(device.Id, device.DeviceName, device.Code, clientState, plcStates, utcNow);

    public static EdgeHostListItemDto ToListItemDto(
        Guid deviceId,
        string hostName,
        string clientCode,
        DeviceClientState? clientState,
        IReadOnlyList<EdgeHostPlcRuntimeState> plcStates,
        DateTime utcNow)
    {
        var softwareStatus = DeviceClientSoftwareStatusResolver.Resolve(clientState, utcNow);
        var lastPlcSeenAtUtc = plcStates.Count == 0
            ? (DateTime?)null
            : plcStates.Max(state => state.LastSeenAtUtc);
        var issue = ResolveIssue(softwareStatus.Issue, plcStates);

        return new EdgeHostListItemDto(
            deviceId,
            deviceId,
            clientCode,
            hostName,
            ResolvePrimaryIp(clientState),
            ResolveLocalIpAddresses(clientState),
            softwareStatus.SoftwareStatus,
            BuildCurrentVersion(clientState),
            clientState?.LastRuntimeHeartbeatAtUtc,
            plcStates.Count,
            plcStates.Count(state => state.RuntimeStatus == EdgeHostPlcRuntimeStatus.Connected),
            plcStates.Count(state => state.RuntimeStatus == EdgeHostPlcRuntimeStatus.Faulted),
            lastPlcSeenAtUtc,
            issue);
    }

    public static EdgeHostDto ToDetailDto(
        Device device,
        DeviceClientState? clientState,
        IReadOnlyList<EdgeHostPlcRuntimeState> plcStates,
        DateTime utcNow)
    {
        var listItem = ToListItemDto(device, clientState, plcStates, utcNow);
        return new EdgeHostDto(
            listItem.Id,
            listItem.DeviceId,
            listItem.ClientCode,
            listItem.HostName,
            listItem.PrimaryIpAddress,
            listItem.LocalIpAddresses,
            listItem.SoftwareStatus,
            listItem.CurrentVersion,
            listItem.LastRuntimeHeartbeatAtUtc,
            listItem.PlcCount,
            listItem.ConnectedPlcCount,
            listItem.FaultedPlcCount,
            listItem.LastPlcSeenAtUtc,
            listItem.Issue,
            plcStates
                .OrderByDescending(state => state.LastSeenAtUtc)
                .ThenBy(state => state.PlcCode, StringComparer.OrdinalIgnoreCase)
                .Select(ToRuntimeStateDto)
                .ToList());
    }

    public static EdgeHostPlcRuntimeStateDto ToRuntimeStateDto(EdgeHostPlcRuntimeState state)
    {
        return new EdgeHostPlcRuntimeStateDto(
            state.Id,
            state.DeviceId,
            state.ClientCode,
            state.PlcCode,
            state.ReportedPlcName,
            state.StationCode,
            state.Protocol,
            state.Address,
            state.IsConnected,
            state.RuntimeStatus,
            state.LastError,
            state.LastSeenAtUtc,
            state.UpdatedAtUtc);
    }

    private static string? ResolveIssue(
        string? softwareIssue,
        IReadOnlyList<EdgeHostPlcRuntimeState> plcStates)
    {
        if (!string.IsNullOrWhiteSpace(softwareIssue))
        {
            return softwareIssue;
        }

        if (plcStates.Count == 0)
        {
            return "客户端尚未上报 PLC 清单。";
        }

        var faulted = plcStates.FirstOrDefault(state => state.RuntimeStatus == EdgeHostPlcRuntimeStatus.Faulted);
        return faulted is null
            ? null
            : $"PLC {faulted.PlcCode} 状态异常。";
    }

    private static string? BuildCurrentVersion(DeviceClientState? state)
    {
        var hostVersion = state?.RuntimeHostVersion ?? state?.HostVersion;
        return string.IsNullOrWhiteSpace(hostVersion)
            ? null
            : $"宿主 {hostVersion}";
    }

    private static string? ResolvePrimaryIp(DeviceClientState? state)
    {
        var runtimeIps = state?.GetRuntimeLocalIpAddresses() ?? [];
        var versionIps = state?.GetVersionLocalIpAddresses() ?? [];
        return runtimeIps.FirstOrDefault()
            ?? NormalizeOptional(state?.RuntimeRemoteIpAddress)
            ?? versionIps.FirstOrDefault()
            ?? NormalizeOptional(state?.VersionRemoteIpAddress);
    }

    private static IReadOnlyList<string> ResolveLocalIpAddresses(DeviceClientState? state)
    {
        var runtimeIps = state?.GetRuntimeLocalIpAddresses() ?? [];
        if (runtimeIps.Count > 0)
        {
            return runtimeIps;
        }

        return state?.GetVersionLocalIpAddresses() ?? [];
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
