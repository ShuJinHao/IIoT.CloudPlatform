using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Events.PassStations;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.Contracts.Uploads;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.PassStations;

/// <summary>
/// 过站接收服务。
/// 负责对设备端上报的过站数据做统一入口校验，校验通过后登记接收并写入 Outbox。
/// </summary>
public sealed class PassStationReceiveService(
    IDeviceIdentityQueryService deviceIdentityQuery,
    IUploadReceiveRegistry uploadReceiveRegistry) : IPassStationReceiveService
{
    public async Task<Result<EdgeUploadAcceptedResponse>> ValidateAndRegisterAsync(
        Guid deviceId,
        int itemCount,
        string messageType,
        string? requestId,
        string deduplicationKey,
        string contentFingerprint,
        IPassStationEvent @event,
        CancellationToken cancellationToken)
    {
        if (deviceId == Guid.Empty)
            return Result.Failure("数据接收失败: DeviceId 不能为空");

        if (itemCount == 0)
            return Result.Failure("数据接收失败: 过站数据列表不能为空");

        var device = await deviceIdentityQuery.GetByDeviceIdAsync(deviceId, cancellationToken);
        if (device is null)
            return Result.Failure("数据接收失败: 设备不存在");

        var registeredProcess = NormalizeProcessType(device.ProcessCode);
        if (registeredProcess is null)
            return Result.Failure("数据接收失败: 设备未登记有效工序");
        if (!string.Equals(registeredProcess, @event.TypeKey, StringComparison.Ordinal)
            || !string.Equals(registeredProcess, @event.ProcessType, StringComparison.Ordinal))
        {
            return Result.Forbidden("数据接收失败: 设备登记工序与上报工序不一致");
        }

        var registration = await uploadReceiveRegistry.RegisterAndEnqueueAsync(
            deviceId,
            messageType,
            requestId,
            deduplicationKey,
            @event,
            cancellationToken,
            contentFingerprint);
        return Result.Success(registration.IsDuplicate
            ? EdgeUploadAcceptedResponse.Duplicate(registration.OutboxMessageId)
            : EdgeUploadAcceptedResponse.Accepted(registration.OutboxMessageId));
    }

    private static string? NormalizeProcessType(string? value)
    {
        value = value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value.ToLowerInvariant();
    }
}
