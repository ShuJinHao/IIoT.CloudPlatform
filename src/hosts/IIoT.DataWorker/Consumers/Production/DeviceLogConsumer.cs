using IIoT.ProductionService.Commands.DeviceLogs;
using IIoT.Services.Common.Events.DeviceLogs;
using MassTransit;
using MediatR;

namespace IIoT.DataWorker.Consumers;

/// <summary>
/// 设备日志事件消费者。
/// 把日志事件转成内部落库命令；命令失败时抛异常，让 MassTransit 接管重试和错误队列。
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
