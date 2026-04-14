using IIoT.ProductionService.Commands.DeviceLogs;
using IIoT.Services.Common.Events.DeviceLogs;
using MassTransit;
using MediatR;

namespace IIoT.DataWorker.Consumers;

/// <summary>
/// 设备日志入库消费者。
/// Handler 成功返回后确认消息；抛异常时交给 MassTransit 重试。
/// </summary>
public sealed class DeviceLogConsumer(ISender sender)
    : IConsumer<DeviceLogReceivedEvent>
{
    public async Task Consume(ConsumeContext<DeviceLogReceivedEvent> context)
    {
        var command = new PersistDeviceLogCommand(context.Message);
        var result = await sender.Send(command, context.CancellationToken);

        if (!result.IsSuccess)
            throw new InvalidOperationException(
                $"设备日志批量落库失败: {string.Join("; ", result.Errors ?? [])}");
    }
}
