using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.RecordQueries;
using IIoT.Services.Common.Events.PassStations;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.PassStations;

/// <summary>
/// 过站接收服务。
/// 负责对设备端上报的过站数据做统一的入口校验，校验通过后再发布到事件总线。
/// </summary>
public sealed class PassStationReceiveService(
    IDeviceIdentityQueryService deviceIdentityQuery,
    IEventPublisher eventPublisher) : IPassStationReceiveService
{
    public async Task<Result<bool>> ValidateAndPublishAsync<TEvent>(
        Guid deviceId,
        int itemCount,
        TEvent @event,
        CancellationToken cancellationToken)
        where TEvent : class, IPassStationEvent
    {
        if (deviceId == Guid.Empty)
            return Result.Failure("数据接收失败:DeviceId 不能为空");

        if (itemCount == 0)
            return Result.Failure("数据接收失败:过站数据列表不能为空");

        var exists = await deviceIdentityQuery.ExistsAsync(deviceId, cancellationToken);
        if (!exists)
            return Result.Failure("数据接收失败:设备不存在");

        await eventPublisher.PublishAsync(@event, cancellationToken);
        return Result.Success(true);
    }
}
