using IIoT.ProductionService.Commands.Capacities;
using IIoT.Services.Common.Events.Capacities;
using MassTransit;
using MediatR;

namespace IIoT.DataWorker.Consumers;

/// <summary>
/// 半小时产能入库消费者。
/// Handler 成功返回后确认消息；抛异常时交给 MassTransit 重试。
/// </summary>
public sealed class HourlyCapacityConsumer(ISender sender)
    : IConsumer<HourlyCapacityReceivedEvent>
{
    public async Task Consume(ConsumeContext<HourlyCapacityReceivedEvent> context)
    {
        var command = new PersistHourlyCapacityCommand(context.Message);
        var result = await sender.Send(command, context.CancellationToken);

        if (!result.IsSuccess)
            throw new InvalidOperationException(
                $"半小时产能落库失败: {string.Join("; ", result.Errors ?? [])}");
    }
}
