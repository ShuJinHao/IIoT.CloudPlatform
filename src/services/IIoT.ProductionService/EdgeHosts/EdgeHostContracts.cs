using IIoT.Core.Production.Aggregates.EdgeHosts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.RecordQueries;

namespace IIoT.ProductionService.EdgeHosts;

public sealed record EdgeHostListItemDto(
    Guid Id,
    Guid DeviceId,
    string ClientCode,
    string HostName,
    bool Enabled,
    int PlcBindingCount,
    int EnabledPlcBindingCount,
    string? Remark,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record EdgeHostDto(
    Guid Id,
    Guid DeviceId,
    string ClientCode,
    string HostName,
    bool Enabled,
    string? Remark,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<EdgeHostPlcBindingDto> PlcBindings);

public sealed record EdgeHostPlcBindingDto(
    Guid Id,
    string PlcCode,
    string PlcName,
    Guid? ProcessId,
    Guid? BusinessDeviceId,
    string? StationCode,
    string? Protocol,
    string? Address,
    bool Enabled,
    int DisplayOrder,
    string? Remark,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record EdgeHostPlcRuntimeStateDto(
    Guid Id,
    Guid EdgeHostId,
    Guid DeviceId,
    string ClientCode,
    Guid? PlcBindingId,
    string PlcCode,
    string? ReportedPlcName,
    bool IsConfigured,
    bool? ConfigEnabled,
    Guid? ProcessId,
    Guid? BusinessDeviceId,
    string? ConfiguredStationCode,
    string? ConfiguredProtocol,
    string? ConfiguredAddress,
    string? RuntimeStationCode,
    string? RuntimeProtocol,
    string? RuntimeAddress,
    bool IsConnected,
    string RuntimeStatus,
    string? LastError,
    DateTime LastSeenAtUtc,
    DateTime UpdatedAtUtc);

public sealed record EdgeHostPlcCapacitySummaryDto(
    Guid PlcBindingId,
    string PlcCode,
    string PlcName,
    bool BindingEnabled,
    Guid? ProcessId,
    Guid? BusinessDeviceId,
    DateOnly Date,
    bool CanReadCapacity,
    string CapacityStatus,
    DailySummaryDto? Summary);

public sealed record EdgeHostPlcRuntimeStateReportResultDto(
    Guid DeviceId,
    string ClientCode,
    Guid EdgeHostId,
    int ReceivedCount,
    int ConfiguredCount,
    int UnconfiguredCount,
    DateTime ReceivedAtUtc);

public static class EdgeHostMapping
{
    public static EdgeHostListItemDto ToListItemDto(EdgeHost host)
    {
        return new EdgeHostListItemDto(
            host.Id,
            host.DeviceId,
            host.ClientCode,
            host.HostName,
            host.Enabled,
            host.PlcBindings.Count,
            host.PlcBindings.Count(binding => binding.Enabled),
            host.Remark,
            host.CreatedAtUtc,
            host.UpdatedAtUtc);
    }

    public static EdgeHostDto ToDto(EdgeHost host)
    {
        return new EdgeHostDto(
            host.Id,
            host.DeviceId,
            host.ClientCode,
            host.HostName,
            host.Enabled,
            host.Remark,
            host.CreatedAtUtc,
            host.UpdatedAtUtc,
            host.PlcBindings
                .OrderBy(binding => binding.DisplayOrder)
                .ThenBy(binding => binding.PlcCode, StringComparer.OrdinalIgnoreCase)
                .Select(ToDto)
                .ToList());
    }

    private static EdgeHostPlcBindingDto ToDto(EdgeHostPlcBinding binding)
    {
        return new EdgeHostPlcBindingDto(
            binding.Id,
            binding.PlcCode,
            binding.PlcName,
            binding.ProcessId,
            binding.BusinessDeviceId,
            binding.StationCode,
            binding.Protocol,
            binding.Address,
            binding.Enabled,
            binding.DisplayOrder,
            binding.Remark,
            binding.CreatedAtUtc,
            binding.UpdatedAtUtc);
    }

    public static EdgeHostPlcRuntimeStateDto ToRuntimeStateDto(
        EdgeHostPlcRuntimeState state,
        EdgeHostPlcBinding? binding)
    {
        return new EdgeHostPlcRuntimeStateDto(
            state.Id,
            state.EdgeHostId,
            state.DeviceId,
            state.ClientCode,
            binding?.Id,
            state.PlcCode,
            state.ReportedPlcName,
            binding is not null,
            binding?.Enabled,
            binding?.ProcessId,
            binding?.BusinessDeviceId,
            binding?.StationCode,
            binding?.Protocol,
            binding?.Address,
            state.StationCode,
            state.Protocol,
            state.Address,
            state.IsConnected,
            state.RuntimeStatus,
            state.LastError,
            state.LastSeenAtUtc,
            state.UpdatedAtUtc);
    }
}

internal static class EdgeHostAudit
{
    public const string TargetType = "EdgeHost";
    public const string Create = "EdgeHost.Create";
    public const string Update = "EdgeHost.Update";
    public const string Enable = "EdgeHost.Enable";
    public const string Disable = "EdgeHost.Disable";
    public const string Delete = "EdgeHost.Delete";
    public const string PlcBindingAdd = "EdgeHost.PlcBinding.Add";
    public const string PlcBindingUpdate = "EdgeHost.PlcBinding.Update";
    public const string PlcBindingEnable = "EdgeHost.PlcBinding.Enable";
    public const string PlcBindingDisable = "EdgeHost.PlcBinding.Disable";
    public const string PlcBindingRemove = "EdgeHost.PlcBinding.Remove";

    public static AuditTrailEntry Entry(
        ICurrentUser currentUser,
        string operationType,
        string targetIdOrKey,
        bool succeeded,
        string summary,
        string? failureReason = null)
    {
        return new AuditTrailEntry(
            ParseActorUserId(currentUser.Id),
            currentUser.UserName,
            operationType,
            TargetType,
            targetIdOrKey,
            DateTime.UtcNow,
            succeeded,
            summary,
            failureReason);
    }

    private static Guid? ParseActorUserId(string? rawUserId)
    {
        return Guid.TryParse(rawUserId, out var actorUserId)
            ? actorUserId
            : null;
    }
}
