using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Events.PassStations;
using IIoT.Services.Contracts.RecordQueries;
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
    public async Task<Result<bool>> ValidateAndRegisterAsync(
        Guid deviceId,
        int itemCount,
        string messageType,
        string? requestId,
        string deduplicationKey,
        IPassStationEvent @event,
        CancellationToken cancellationToken)
    {
        if (deviceId == Guid.Empty)
            return Result.Failure("数据接收失败: DeviceId 不能为空");

        if (itemCount == 0)
            return Result.Failure("数据接收失败: 过站数据列表不能为空");

        var exists = await deviceIdentityQuery.ExistsAsync(deviceId, cancellationToken);
        if (!exists)
            return Result.Failure("数据接收失败: 设备不存在");

        await uploadReceiveRegistry.RegisterAndEnqueueAsync(
            deviceId,
            messageType,
            requestId,
            deduplicationKey,
            @event,
            cancellationToken);
        return Result.Success(true);
    }
}
