using AutoMapper;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.RecordQueries;
using IIoT.Services.Common.Events.DeviceLogs;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.DeviceLogs;

public record ReceiveDeviceLogCommand(
    Guid DeviceId,
    List<DeviceLogItem> Logs
) : IDeviceCommand<Result<bool>>;

public class ReceiveDeviceLogHandler(
    IDeviceIdentityQueryService deviceIdentityQuery,
    IMapper mapper,
    IEventPublisher eventPublisher
) : ICommandHandler<ReceiveDeviceLogCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        ReceiveDeviceLogCommand request,
        CancellationToken cancellationToken)
    {
        if (request.DeviceId == Guid.Empty)
            return Result.Failure("数据接收失败: DeviceId 不能为空");

        if (request.Logs is null || request.Logs.Count == 0)
            return Result.Failure("数据接收失败: 日志列表不能为空");

        var exists = await deviceIdentityQuery.ExistsAsync(
            request.DeviceId, cancellationToken);
        if (!exists)
            return Result.Failure("数据接收失败: 设备不存在");

        var @event = mapper.Map<DeviceLogReceivedEvent>(request);
        await eventPublisher.PublishAsync(@event, cancellationToken);

        return Result.Success(true);
    }
}
